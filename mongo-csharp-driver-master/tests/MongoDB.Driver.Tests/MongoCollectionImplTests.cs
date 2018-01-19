/* Copyright 2010-2017 MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.TestHelpers.XunitExtensions;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Operations;
using MongoDB.Driver.Core.Servers;
using MongoDB.Driver.Tests;
using Moq;
using Xunit;

namespace MongoDB.Driver
{
    public class MongoCollectionImplTests
    {
        private readonly ConnectionId _connectionId = new ConnectionId(new ServerId(new ClusterId(0), new DnsEndPoint("localhost", 27017)), 0);
        private readonly ReadConcern _readConcern = ReadConcern.Majority;
        private MockOperationExecutor _operationExecutor;

        public MongoCollectionImplTests()
        {
            var mockClient = new Mock<IMongoClient>();
            _operationExecutor = new MockOperationExecutor();
            _operationExecutor.Client = mockClient.Object;
        }

        [Fact]
        public void CollectionName_should_be_set()
        {
            var subject = CreateSubject<BsonDocument>();
            subject.CollectionNamespace.CollectionName.Should().Be("bar");
        }

        public void Database_should_be_set()
        {
            var subject = CreateSubject<BsonDateTime>();
            subject.Database.Should().NotBeNull();
        }

        [Fact]
        public void Settings_should_be_set()
        {
            var subject = CreateSubject<BsonDocument>();
            subject.Settings.Should().NotBeNull();
        }

        [Theory]
        [ParameterAttributeData]
        public void Aggregate_should_execute_an_AggregateOperation_when_out_is_not_specified(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var pipeline = new EmptyPipelineDefinition<BsonDocument>().Match("{ x : 2 }");
            var options = new AggregateOptions()
            {
                AllowDiskUse = true,
                BatchSize = 10,
                Collation = new Collation("en_US"),
                Comment = "test",
                Hint = new BsonDocument("x", 1),
                MaxAwaitTime = TimeSpan.FromSeconds(4),
                MaxTime = TimeSpan.FromSeconds(3),
                UseCursor = false
            };
            var cancellationToken = new CancellationTokenSource().Token;
            var renderedPipeline = RenderPipeline(subject, pipeline);

            if (usingSession)
            {
                if (async)
                {
                    subject.AggregateAsync(session, pipeline, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Aggregate(session, pipeline, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.AggregateAsync(pipeline, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Aggregate(pipeline, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<BsonDocument>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<AggregateOperation<BsonDocument>>().Subject;
            operation.AllowDiskUse.Should().Be(options.AllowDiskUse);
            operation.BatchSize.Should().Be(options.BatchSize);
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Comment.Should().Be(options.Comment);
            operation.Hint.Should().Be(options.Hint);
            operation.MaxAwaitTime.Should().Be(options.MaxAwaitTime);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.Pipeline.Should().Equal(renderedPipeline.Documents);
            operation.ReadConcern.Should().Be(_readConcern);
            operation.ResultSerializer.Should().BeSameAs(renderedPipeline.OutputSerializer);
            operation.UseCursor.Should().Be(options.UseCursor);
        }

        [Theory]
        [ParameterAttributeData]
        public void Aggregate_should_execute_an_AggregateToCollectionOperation_and_a_FindOperation_when_out_is_specified(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var writeConcern = new WriteConcern(1);
            var subject = CreateSubject<BsonDocument>().WithWriteConcern(writeConcern);
            var session = CreateSession(usingSession);
            var pipeline = new EmptyPipelineDefinition<BsonDocument>()
                .Match("{ x : 2 }")
                .Out(subject.Database.GetCollection<BsonDocument>("funny"));
            var options = new AggregateOptions()
            {
                AllowDiskUse = true,
                BatchSize = 10,
                BypassDocumentValidation = true,
                Collation = new Collation("en_US"),
                Comment = "test",
                Hint = new BsonDocument("x", 1),
                MaxTime = TimeSpan.FromSeconds(3),
                UseCursor = false,
            };
            var cancellationToken1 = new CancellationTokenSource().Token;
            var cancellationToken2 = new CancellationTokenSource().Token;
            var renderedPipeline = RenderPipeline(subject, pipeline);

            IAsyncCursor<BsonDocument> result;
            if (usingSession)
            {
                if (async)
                {
                    result = subject.AggregateAsync(session, pipeline, options, cancellationToken1).GetAwaiter().GetResult();
                }
                else
                {
                    result = subject.Aggregate(session, pipeline, options, cancellationToken1);
                }
            }
            else
            {
                if (async)
                {
                    result = subject.AggregateAsync(pipeline, options, cancellationToken1).GetAwaiter().GetResult();
                }
                else
                {
                    result = subject.Aggregate(pipeline, options, cancellationToken1);
                }
            }

            var aggregateCall = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(aggregateCall, session, cancellationToken1);

            var aggregateOperation = aggregateCall.Operation.Should().BeOfType<AggregateToCollectionOperation>().Subject;
            aggregateOperation.AllowDiskUse.Should().Be(options.AllowDiskUse);
            aggregateOperation.BypassDocumentValidation.Should().Be(options.BypassDocumentValidation);
            aggregateOperation.Collation.Should().BeSameAs(options.Collation);
            aggregateOperation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            aggregateOperation.Comment.Should().Be(options.Comment);
            aggregateOperation.Hint.Should().Be(options.Hint);
            aggregateOperation.MaxTime.Should().Be(options.MaxTime);
            aggregateOperation.Pipeline.Should().Equal(renderedPipeline.Documents);
            aggregateOperation.WriteConcern.Should().BeSameAs(writeConcern);

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();
            _operationExecutor.EnqueueResult(mockCursor.Object);

            if (async)
            {
                result.MoveNextAsync(cancellationToken2).GetAwaiter().GetResult();
            }
            else
            {
                result.MoveNext(cancellationToken2);
            }

            var findCall = _operationExecutor.GetReadCall<IAsyncCursor<BsonDocument>>();
            VerifySessionAndCancellationToken(findCall, session, cancellationToken2);

            var findOperation = findCall.Operation.Should().BeOfType<FindOperation<BsonDocument>>().Subject;
            findOperation.AllowPartialResults.Should().NotHaveValue();
            findOperation.BatchSize.Should().Be(options.BatchSize);
            findOperation.Collation.Should().BeSameAs(options.Collation);
            findOperation.CollectionNamespace.FullName.Should().Be("foo.funny");
            findOperation.Comment.Should().BeNull();
            findOperation.CursorType.Should().Be(Core.Operations.CursorType.NonTailable);
            findOperation.Filter.Should().BeNull();
            findOperation.Limit.Should().Be(null);
            findOperation.MaxTime.Should().Be(options.MaxTime);
            findOperation.Modifiers.Should().BeNull();
            findOperation.NoCursorTimeout.Should().NotHaveValue();
            findOperation.OplogReplay.Should().NotHaveValue();
            findOperation.Projection.Should().BeNull();
            findOperation.Skip.Should().Be(null);
            findOperation.Sort.Should().BeNull();
        }

        [Theory]
        [ParameterAttributeData]
        public void BulkWrite_should_execute_a_BulkMixedWriteOperation(
            [Values(false, true)] bool usingSession,
            [Values(null, false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isOrdered,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var collation = new Collation("en_US");
            var requests = new WriteModel<BsonDocument>[]
            {
                new InsertOneModel<BsonDocument>(new BsonDocument("_id", 1).Add("a",1)),
                new DeleteManyModel<BsonDocument>(new BsonDocument("b", 1)) { Collation = collation },
                new DeleteOneModel<BsonDocument>(new BsonDocument("c", 1)) { Collation = collation },
                new ReplaceOneModel<BsonDocument>(new BsonDocument("d", 1), new BsonDocument("e", 1)) { Collation = collation },
                new ReplaceOneModel<BsonDocument>(new BsonDocument("f", 1), new BsonDocument("g", 1)) { Collation = collation, IsUpsert = true },
                new UpdateManyModel<BsonDocument>(new BsonDocument("h", 1), new BsonDocument("$set", new BsonDocument("i", 1))) { Collation = collation },
                new UpdateManyModel<BsonDocument>(new BsonDocument("j", 1), new BsonDocument("$set", new BsonDocument("k", 1))) { Collation = collation, IsUpsert = true },
                new UpdateOneModel<BsonDocument>(new BsonDocument("l", 1), new BsonDocument("$set", new BsonDocument("m", 1))) { Collation = collation },
                new UpdateOneModel<BsonDocument>(new BsonDocument("n", 1), new BsonDocument("$set", new BsonDocument("o", 1))) { Collation = collation, IsUpsert = true },
            };
            var options = new BulkWriteOptions
            {
                BypassDocumentValidation = bypassDocumentValidation,
                IsOrdered = isOrdered
            };
            var cancellationToken = new CancellationTokenSource().Token;

            var operationResult = new BulkWriteOperationResult.Unacknowledged(9, new[] { new InsertRequest(new BsonDocument("b", 1)) });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            BulkWriteResult<BsonDocument> result;
            if (usingSession)
            {
                if (async)
                {
                    result = subject.BulkWriteAsync(session, requests, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    result = subject.BulkWrite(session, requests, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    result = subject.BulkWriteAsync(requests, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    result = subject.BulkWrite(requests, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            // I know, this is a lot of stuff in one test :(
            var operation = call.Operation.Should().BeOfType<BulkMixedWriteOperation>().Subject;
            operation.BypassDocumentValidation.Should().Be(bypassDocumentValidation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.IsOrdered.Should().Be(isOrdered);
            operation.Requests.Count().Should().Be(9);

            var convertedRequests = operation.Requests.ToList();

            // InsertOneModel
            convertedRequests[0].Should().BeOfType<InsertRequest>();
            convertedRequests[0].CorrelationId.Should().Be(0);
            var convertedRequest0 = (InsertRequest)convertedRequests[0];
            convertedRequest0.Document.Should().Be("{_id:1, a:1}");

            // RemoveManyModel
            convertedRequests[1].Should().BeOfType<DeleteRequest>();
            convertedRequests[1].CorrelationId.Should().Be(1);
            var convertedRequest1 = (DeleteRequest)convertedRequests[1];
            convertedRequest1.Collation.Should().BeSameAs(collation);
            convertedRequest1.Filter.Should().Be("{b:1}");
            convertedRequest1.Limit.Should().Be(0);

            // RemoveOneModel
            convertedRequests[2].Should().BeOfType<DeleteRequest>();
            convertedRequests[2].CorrelationId.Should().Be(2);
            var convertedRequest2 = (DeleteRequest)convertedRequests[2];
            convertedRequest2.Collation.Should().BeSameAs(collation);
            convertedRequest2.Filter.Should().Be("{c:1}");
            convertedRequest2.Limit.Should().Be(1);

            // ReplaceOneModel
            convertedRequests[3].Should().BeOfType<UpdateRequest>();
            convertedRequests[3].CorrelationId.Should().Be(3);
            var convertedRequest3 = (UpdateRequest)convertedRequests[3];
            convertedRequest3.Collation.Should().BeSameAs(collation);
            convertedRequest3.Filter.Should().Be("{d:1}");
            convertedRequest3.Update.Should().Be("{e:1}");
            convertedRequest3.UpdateType.Should().Be(UpdateType.Replacement);
            convertedRequest3.IsMulti.Should().BeFalse();
            convertedRequest3.IsUpsert.Should().BeFalse();

            // ReplaceOneModel with upsert
            convertedRequests[4].Should().BeOfType<UpdateRequest>();
            convertedRequests[4].CorrelationId.Should().Be(4);
            var convertedRequest4 = (UpdateRequest)convertedRequests[4];
            convertedRequest4.Collation.Should().BeSameAs(collation);
            convertedRequest4.Filter.Should().Be("{f:1}");
            convertedRequest4.Update.Should().Be("{g:1}");
            convertedRequest4.UpdateType.Should().Be(UpdateType.Replacement);
            convertedRequest4.IsMulti.Should().BeFalse();
            convertedRequest4.IsUpsert.Should().BeTrue();

            // UpdateManyModel
            convertedRequests[5].Should().BeOfType<UpdateRequest>();
            convertedRequests[5].CorrelationId.Should().Be(5);
            var convertedRequest5 = (UpdateRequest)convertedRequests[5];
            convertedRequest5.Collation.Should().BeSameAs(collation);
            convertedRequest5.Filter.Should().Be("{h:1}");
            convertedRequest5.Update.Should().Be("{$set:{i:1}}");
            convertedRequest5.UpdateType.Should().Be(UpdateType.Update);
            convertedRequest5.IsMulti.Should().BeTrue();
            convertedRequest5.IsUpsert.Should().BeFalse();

            // UpdateManyModel with upsert
            convertedRequests[6].Should().BeOfType<UpdateRequest>();
            convertedRequests[6].CorrelationId.Should().Be(6);
            var convertedRequest6 = (UpdateRequest)convertedRequests[6];
            convertedRequest6.Collation.Should().BeSameAs(collation);
            convertedRequest6.Filter.Should().Be("{j:1}");
            convertedRequest6.Update.Should().Be("{$set:{k:1}}");
            convertedRequest6.UpdateType.Should().Be(UpdateType.Update);
            convertedRequest6.IsMulti.Should().BeTrue();
            convertedRequest6.IsUpsert.Should().BeTrue();

            // UpdateOneModel
            convertedRequests[7].Should().BeOfType<UpdateRequest>();
            convertedRequests[7].CorrelationId.Should().Be(7);
            var convertedRequest7 = (UpdateRequest)convertedRequests[7];
            convertedRequest7.Collation.Should().BeSameAs(collation);
            convertedRequest7.Filter.Should().Be("{l:1}");
            convertedRequest7.Update.Should().Be("{$set:{m:1}}");
            convertedRequest7.UpdateType.Should().Be(UpdateType.Update);
            convertedRequest7.IsMulti.Should().BeFalse();
            convertedRequest7.IsUpsert.Should().BeFalse();

            // UpdateOneModel with upsert
            convertedRequests[8].Should().BeOfType<UpdateRequest>();
            convertedRequests[8].CorrelationId.Should().Be(8);
            var convertedRequest8 = (UpdateRequest)convertedRequests[8];
            convertedRequest8.Collation.Should().BeSameAs(collation);
            convertedRequest8.Filter.Should().Be("{n:1}");
            convertedRequest8.Update.Should().Be("{$set:{o:1}}");
            convertedRequest8.UpdateType.Should().Be(UpdateType.Update);
            convertedRequest8.IsMulti.Should().BeFalse();
            convertedRequest8.IsUpsert.Should().BeTrue();

            // Result
            result.Should().NotBeNull();
            result.IsAcknowledged.Should().BeFalse();
            result.RequestCount.Should().Be(9);
            result.ProcessedRequests.Should().BeEquivalentTo(requests);
            for (int i = 0; i < requests.Length; i++)
            {
                result.ProcessedRequests[i].Should().BeSameAs(requests[i]);
            }
        }

        [Theory]
        [ParameterAttributeData]
        public void Count_should_execute_a_CountOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filter = new BsonDocument("x", 1);
            var options = new CountOptions
            {
                Collation = new Collation("en_US"),
                Hint = "funny",
                Limit = 10,
                MaxTime = TimeSpan.FromSeconds(20),
                Skip = 30
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.CountAsync(session, filter, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Count(session, filter, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.CountAsync(filter, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Count(filter, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<long>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<CountOperation>().Subject;
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Filter.Should().Be(filter);
            operation.Hint.Should().Be(options.Hint);
            operation.Limit.Should().Be(options.Limit);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.ReadConcern.Should().Be(_readConcern);
            operation.Skip.Should().Be(options.Skip);
        }

        [Theory]
        [ParameterAttributeData]
        public void DeleteMany_should_execute_a_BulkMixedOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filter = new BsonDocument("a", 1);
            var collation = new Collation("en_US");
            var options = new DeleteOptions { Collation = collation };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new DeleteRequest(filter) { Collation = collation, CorrelationId = 0, Limit = 0 };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(9, new[] { processedRequest });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.DeleteManyAsync(session, filter, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.DeleteMany(session, filter, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.DeleteManyAsync(filter, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.DeleteMany(filter, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifySingleWrite(call, null, true, processedRequest);
        }

        [Theory]
        [ParameterAttributeData]
        public void DeleteMany_should_throw_a_WriteException_when_an_error_occurs(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filter = new BsonDocument("a", 1);
            var options = new DeleteOptions();
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new DeleteRequest(filter) { CorrelationId = 0, Limit = 0 };
            var operationException = new MongoBulkWriteOperationException(
                _connectionId,
                new BulkWriteOperationResult.Acknowledged(
                    requestCount: 1,
                    matchedCount: 1,
                    deletedCount: 0,
                    insertedCount: 0,
                    modifiedCount: 0,
                    processedRequests: new[] { processedRequest },
                    upserts: new List<BulkWriteOperationUpsert>()),
                new[] { new BulkWriteOperationError(10, 1, "blah", new BsonDocument()) },
                null,
                new List<WriteRequest>());
            _operationExecutor.EnqueueException<BulkWriteOperationResult>(operationException);

            Exception exception;
            if (usingSession)
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.DeleteManyAsync(session, filter, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.DeleteMany(session, filter, options, cancellationToken));
                }
            }
            else
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.DeleteManyAsync(filter, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.DeleteMany(filter, options, cancellationToken));
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            exception.Should().BeOfType<MongoWriteException>();
        }

        [Theory]
        [ParameterAttributeData]
        public void DeleteOne_should_execute_a_BulkMixedOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filter = new BsonDocument("a", 1);
            var collation = new Collation("en_US");
            var options = new DeleteOptions { Collation = collation };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new DeleteRequest(filter) { Collation = collation, CorrelationId = 0, Limit = 1 };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(9, new[] { processedRequest });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.DeleteOneAsync(session, filter, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.DeleteOne(session, filter, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.DeleteOneAsync(filter, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.DeleteOne(filter, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifySingleWrite(call, null, true, processedRequest);
        }

        [Theory]
        [ParameterAttributeData]
        public void DeleteOne_should_throw_a_WriteException_when_an_error_occurs(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filter = new BsonDocument("a", 1);
            var options = new DeleteOptions();
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new DeleteRequest(filter) { CorrelationId = 0, Limit = 1 };
            var operationException = new MongoBulkWriteOperationException(
                _connectionId,
                new BulkWriteOperationResult.Acknowledged(
                    requestCount: 1,
                    matchedCount: 1,
                    deletedCount: 0,
                    insertedCount: 0,
                    modifiedCount: 0,
                    processedRequests: new[] { processedRequest },
                    upserts: new List<BulkWriteOperationUpsert>()),
                new[] { new BulkWriteOperationError(0, 1, "blah", new BsonDocument()) },
                null,
                new List<WriteRequest>());
            _operationExecutor.EnqueueException<BulkWriteOperationResult>(operationException);

            Exception exception;
            if (usingSession)
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.DeleteOneAsync(session, filter, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.DeleteOne(session, filter, options, cancellationToken));
                }
            }
            else
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.DeleteOneAsync(filter, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.DeleteOne(filter, options, cancellationToken));
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            exception.Should().BeOfType<MongoWriteException>();
        }

        [Theory]
        [ParameterAttributeData]
        public void Distinct_should_execute_a_DistinctOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var fieldName = "a.b";
            var fieldDefinition = (FieldDefinition<BsonDocument, int>)fieldName;
            var filterDocument = new BsonDocument("x", 1);
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var options = new DistinctOptions
            {
                Collation = new Collation("en_US"),
                MaxTime = TimeSpan.FromSeconds(20)
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.DistinctAsync(session, fieldDefinition, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Distinct(session, fieldDefinition, filterDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.DistinctAsync(fieldDefinition, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Distinct(fieldDefinition, filterDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<int>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<DistinctOperation<int>>().Subject;
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.FieldName.Should().Be(fieldName);
            operation.Filter.Should().Be(filterDocument);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.ReadConcern.Should().Be(_readConcern);
            operation.ValueSerializer.ValueType.Should().Be(typeof(int));
        }

        private enum EnumForDistinctWithArrayField { A, B }

        private class ClassForDistinctWithArrayField
        {
            public int Id { get; set; }
            [BsonRepresentation(BsonType.String)]
            public EnumForDistinctWithArrayField[] A { get; set; }
        }

        [Theory]
        [ParameterAttributeData]
        public void Distinct_should_execute_a_DistinctOperation_when_type_parameter_is_array_field_item_type(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<ClassForDistinctWithArrayField>();
            var session = CreateSession(usingSession);
            var fieldName = "A";
            var fieldDefinition = (FieldDefinition<ClassForDistinctWithArrayField, EnumForDistinctWithArrayField>)fieldName;
            var filterDocument = new BsonDocument("x", 1);
            var filterDefinition = (FilterDefinition<ClassForDistinctWithArrayField>)filterDocument;
            var options = new DistinctOptions
            {
                Collation = new Collation("en_US"),
                MaxTime = TimeSpan.FromSeconds(20)
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.DistinctAsync(session, fieldDefinition, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Distinct(session, fieldDefinition, filterDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.DistinctAsync(fieldDefinition, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Distinct(fieldDefinition, filterDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<EnumForDistinctWithArrayField>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<DistinctOperation<EnumForDistinctWithArrayField>>().Subject;
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.FieldName.Should().Be(fieldName);
            operation.Filter.Should().Be(filterDocument);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.ReadConcern.Should().Be(_readConcern);

            var documentSerializer = BsonSerializer.SerializerRegistry.GetSerializer<ClassForDistinctWithArrayField>();
            BsonSerializationInfo fieldSerializationInfo;
            ((IBsonDocumentSerializer)documentSerializer).TryGetMemberSerializationInfo(fieldName, out fieldSerializationInfo).Should().BeTrue();
            var fieldSerializer = (ArraySerializer<EnumForDistinctWithArrayField>)fieldSerializationInfo.Serializer;
            operation.ValueSerializer.Should().BeSameAs(fieldSerializer.ItemSerializer);
        }

        [Theory]
        [ParameterAttributeData]
        public void Distinct_should_execute_a_DistinctOperation_when_type_parameter_is_string_instead_of_enum(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<ClassForDistinctWithArrayField>();
            var session = CreateSession(usingSession);
            var fieldName = "A";
            var fieldDefinition = (FieldDefinition<ClassForDistinctWithArrayField, string>)fieldName;
            var filterDocument = new BsonDocument("x", 1);
            var filterDefinition = (FilterDefinition<ClassForDistinctWithArrayField>)filterDocument;
            var options = new DistinctOptions
            {
                Collation = new Collation("en_US"),
                MaxTime = TimeSpan.FromSeconds(20)
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.DistinctAsync(session, fieldDefinition, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Distinct(session, fieldDefinition, filterDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.DistinctAsync(fieldDefinition, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Distinct(fieldDefinition, filterDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<string>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<DistinctOperation<string>>().Subject;
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.FieldName.Should().Be(fieldName);
            operation.Filter.Should().Be(filterDocument);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.ReadConcern.Should().Be(_readConcern);

            var stringSerializer = BsonSerializer.SerializerRegistry.GetSerializer<string>();
            operation.ValueSerializer.Should().BeSameAs(stringSerializer);
        }

        [Theory]
        [ParameterAttributeData]
        public void Find_should_execute_a_FindOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ x : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var projectionDocument = BsonDocument.Parse("{ y : 1 }");
            var projectionDefinition = (ProjectionDefinition<BsonDocument, BsonDocument>)projectionDocument;
            var sortDocument = BsonDocument.Parse("{ a : 1 }");
            var sortDefinition = (SortDefinition<BsonDocument>)sortDocument;
            var options = new FindOptions<BsonDocument, BsonDocument>
            {
                AllowPartialResults = true,
                BatchSize = 20,
                Collation = new Collation("en_US"),
                Comment = "funny",
                CursorType = CursorType.TailableAwait,
                Limit = 30,
                MaxAwaitTime = TimeSpan.FromSeconds(4),
                MaxTime = TimeSpan.FromSeconds(3),
                Modifiers = BsonDocument.Parse("{ $snapshot : true }"),
                NoCursorTimeout = true,
                OplogReplay = true,
                Projection = projectionDefinition,
                Skip = 40,
                Sort = sortDefinition
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindAsync(session, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindSync(session, filterDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindAsync(filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindSync(filterDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<BsonDocument>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            call.Operation.Should().BeOfType<FindOperation<BsonDocument>>();
            var operation = (FindOperation<BsonDocument>)call.Operation;
            operation.AllowPartialResults.Should().Be(options.AllowPartialResults);
            operation.BatchSize.Should().Be(options.BatchSize);
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Comment.Should().Be("funny");
            operation.CursorType.Should().Be(MongoDB.Driver.Core.Operations.CursorType.TailableAwait);
            operation.Filter.Should().Be(filterDocument);
            operation.Limit.Should().Be(options.Limit);
            operation.MaxAwaitTime.Should().Be(options.MaxAwaitTime);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.Modifiers.Should().Be(options.Modifiers);
            operation.NoCursorTimeout.Should().Be(options.NoCursorTimeout);
            operation.OplogReplay.Should().Be(options.OplogReplay);
            operation.Projection.Should().Be(projectionDocument);
            operation.ReadConcern.Should().Be(_readConcern);
            operation.ResultSerializer.ValueType.Should().Be(typeof(BsonDocument));
            operation.Skip.Should().Be(options.Skip);
            operation.Sort.Should().Be(sortDocument);
        }

        [Theory]
        [ParameterAttributeData]
        public void Find_with_an_expression_execute_a_FindOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterExpression = (Expression<Func<BsonDocument, bool>>)(doc => doc["x"] == 1);
            var projectionDocument = BsonDocument.Parse("{ y : 1 }");
            var projectionDefinition = (ProjectionDefinition<BsonDocument, BsonDocument>)projectionDocument;
            var sortDocument = BsonDocument.Parse("{ a : 1 }");
            var sortDefinition = (SortDefinition<BsonDocument>)sortDocument;
            var options = new FindOptions<BsonDocument, BsonDocument>
            {
                AllowPartialResults = true,
                BatchSize = 20,
                Collation = new Collation("en_US"),
                Comment = "funny",
                CursorType = CursorType.TailableAwait,
                Limit = 30,
                MaxAwaitTime = TimeSpan.FromSeconds(4),
                MaxTime = TimeSpan.FromSeconds(3),
                Modifiers = BsonDocument.Parse("{ $snapshot : true }"),
                NoCursorTimeout = true,
                OplogReplay = true,
                Projection = projectionDefinition,
                Skip = 40,
                Sort = sortDefinition
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindAsync(session, filterExpression, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindSync(session, filterExpression, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindAsync(filterExpression, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindSync(filterExpression, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<BsonDocument>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<FindOperation<BsonDocument>>().Subject;
            operation.AllowPartialResults.Should().Be(options.AllowPartialResults);
            operation.BatchSize.Should().Be(options.BatchSize);
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Comment.Should().Be("funny");
            operation.CursorType.Should().Be(MongoDB.Driver.Core.Operations.CursorType.TailableAwait);
            operation.Filter.Should().Be(new BsonDocument("x", 1));
            operation.Limit.Should().Be(options.Limit);
            operation.MaxAwaitTime.Should().Be(options.MaxAwaitTime);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.Modifiers.Should().Be(options.Modifiers);
            operation.NoCursorTimeout.Should().Be(options.NoCursorTimeout);
            operation.OplogReplay.Should().Be(options.OplogReplay);
            operation.Projection.Should().Be(projectionDocument);
            operation.ReadConcern.Should().Be(_readConcern);
            operation.ResultSerializer.ValueType.Should().Be(typeof(BsonDocument));
            operation.Skip.Should().Be(options.Skip);
            operation.Sort.Should().Be(sortDocument);
        }

        [Theory]
        [ParameterAttributeData]
        public void Find_with_Projection_As_should_execute_correctly(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<A>();
            var session = CreateSession(usingSession);
            var filterDefinition = Builders<A>.Filter.Empty;
            var options = new FindOptions<A, BsonDocument>
            {
                Projection = Builders<A>.Projection.As<BsonDocument>()
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindAsync(session, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindSync(session, filterDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindAsync(filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindSync(filterDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<BsonDocument>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<FindOperation<BsonDocument>>().Subject;
            operation.Projection.Should().BeNull();
            operation.ResultSerializer.Should().BeOfType<BsonDocumentSerializer>();
            operation.ReadConcern.Should().Be(_readConcern);
        }

        [Theory]
        [ParameterAttributeData]
        public void FindOneAndDelete_should_execute_a_FindOneAndDeleteOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ x : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var projectionDocument = BsonDocument.Parse("{ x : 1 }");
            var projectionDefinition = (ProjectionDefinition<BsonDocument, BsonDocument>)projectionDocument;
            var sortDocument = BsonDocument.Parse("{ a : -1 } ");
            var sortDefinition = (SortDefinition<BsonDocument>)sortDocument;
            var options = new FindOneAndDeleteOptions<BsonDocument, BsonDocument>
            {
                Collation = new Collation("en_US"),
                Projection = projectionDefinition,
                Sort = sortDefinition,
                MaxTime = TimeSpan.FromSeconds(2)
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindOneAndDeleteAsync(session, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndDelete(session, filterDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindOneAndDeleteAsync(filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndDelete(filterDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<FindOneAndDeleteOperation<BsonDocument>>().Subject;
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Filter.Should().Be(filterDocument);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.Projection.Should().Be(projectionDocument);
            operation.ResultSerializer.Should().BeOfType<FindAndModifyValueDeserializer<BsonDocument>>();
            operation.Sort.Should().Be(sortDocument);
            operation.WriteConcern.Should().BeSameAs(subject.Settings.WriteConcern);
        }

        [Theory]
        [ParameterAttributeData]
        public void FindOneAndDelete_with_Projection_As_should_execute_correctly(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<A>();
            var session = CreateSession(usingSession);
            var filterDefinition = Builders<A>.Filter.Empty;
            var options = new FindOneAndDeleteOptions<A, BsonDocument>
            {
                Collation = new Collation("en_US"),
                Projection = Builders<A>.Projection.As<BsonDocument>()
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindOneAndDeleteAsync(session, filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndDelete(session, filterDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindOneAndDeleteAsync(filterDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndDelete(filterDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<FindOneAndDeleteOperation<BsonDocument>>().Subject;
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.Projection.Should().BeNull();
            operation.ResultSerializer.Should().BeOfType<FindAndModifyValueDeserializer<BsonDocument>>();
        }

        [Theory]
        [ParameterAttributeData]
        public void FindOneAndReplace_should_execute_a_FindOneAndReplaceOperation(
            [Values(false, true)] bool usingSession,
            [Values(null, false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isUpsert,
            [Values(ReturnDocument.After, ReturnDocument.Before)] ReturnDocument returnDocument,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ x : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var replacement = BsonDocument.Parse("{ a : 2 }");
            var projectionDocument = BsonDocument.Parse("{ x : 1 }");
            var projectionDefinition = (ProjectionDefinition<BsonDocument, BsonDocument>)projectionDocument;
            var sortDocument = BsonDocument.Parse("{ a : -1 }");
            var sortDefinition = (SortDefinition<BsonDocument>)sortDocument;
            var options = new FindOneAndReplaceOptions<BsonDocument, BsonDocument>()
            {
                BypassDocumentValidation = bypassDocumentValidation,
                Collation = new Collation("en_US"),
                IsUpsert = isUpsert,
                MaxTime = TimeSpan.FromSeconds(2),
                Projection = projectionDefinition,
                ReturnDocument = returnDocument,
                Sort = sortDefinition
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindOneAndReplaceAsync(session, filterDefinition, replacement, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndReplace(session, filterDefinition, replacement, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindOneAndReplaceAsync(filterDefinition, replacement, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndReplace(filterDefinition, replacement, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<FindOneAndReplaceOperation<BsonDocument>>().Subject;
            operation.BypassDocumentValidation.Should().Be(bypassDocumentValidation);
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Filter.Should().Be(filterDocument);
            operation.IsUpsert.Should().Be(isUpsert);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.Projection.Should().Be(projectionDocument);
            operation.Replacement.Should().Be(replacement);
            operation.ResultSerializer.Should().BeOfType<FindAndModifyValueDeserializer<BsonDocument>>();
            operation.ReturnDocument.Should().Be((Core.Operations.ReturnDocument)returnDocument);
            operation.Sort.Should().Be(sortDocument);
            operation.WriteConcern.Should().BeSameAs(subject.Settings.WriteConcern);
        }

        [Theory]
        [ParameterAttributeData]
        public void FindOneAndReplace_with_Projection_As_should_execute_correctly(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<A>();
            var session = CreateSession(usingSession);
            var filterDefinition = Builders<A>.Filter.Empty;
            var replacement = new A();
            var options = new FindOneAndReplaceOptions<A, BsonDocument>
            {
                Collation = new Collation("en_US"),
                Projection = Builders<A>.Projection.As<BsonDocument>()
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindOneAndReplaceAsync(session, filterDefinition, replacement, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndReplace(session, filterDefinition, replacement, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindOneAndReplaceAsync(filterDefinition, replacement, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndReplace(filterDefinition, replacement, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<FindOneAndReplaceOperation<BsonDocument>>().Subject;
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.Projection.Should().BeNull();
            operation.ResultSerializer.Should().BeOfType<FindAndModifyValueDeserializer<BsonDocument>>();
        }

        [Theory]
        [ParameterAttributeData]
        public void FindOneAndUpdate_should_execute_a_FindOneAndUpdateOperation(
            [Values(false, true)] bool usingSession,
            [Values(null, false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isUpsert,
            [Values(ReturnDocument.After, ReturnDocument.Before)] ReturnDocument returnDocument,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ x : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var updateDocument = BsonDocument.Parse("{ $set : { a : 2 } }");
            var updateDefinition = (UpdateDefinition<BsonDocument>)updateDocument;
            var arrayFilterDocument = BsonDocument.Parse("{ b : 1 }");
            var arrayFilterDefinition = (ArrayFilterDefinition<BsonDocument>)arrayFilterDocument;
            var projectionDocument = BsonDocument.Parse("{ x : 1 }");
            var projectionDefinition = (ProjectionDefinition<BsonDocument, BsonDocument>)projectionDocument;
            var sortDocument = BsonDocument.Parse("{ a : -1 }");
            var sortDefinition = (SortDefinition<BsonDocument>)sortDocument;
            var options = new FindOneAndUpdateOptions<BsonDocument, BsonDocument>()
            {
                ArrayFilters = new[] { arrayFilterDefinition },
                BypassDocumentValidation = bypassDocumentValidation,
                Collation = new Collation("en_US"),
                IsUpsert = isUpsert,
                MaxTime = TimeSpan.FromSeconds(2),
                Projection = projectionDefinition,
                ReturnDocument = returnDocument,
                Sort = sortDefinition,
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindOneAndUpdateAsync(session, filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndUpdate(session, filterDefinition, updateDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindOneAndUpdateAsync(filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndUpdate(filterDefinition, updateDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<FindOneAndUpdateOperation<BsonDocument>>().Subject;
            operation.ArrayFilters.Should().Equal(new[] { arrayFilterDocument });
            operation.BypassDocumentValidation.Should().Be(bypassDocumentValidation);
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Filter.Should().Be(filterDocument);
            operation.IsUpsert.Should().Be(isUpsert);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.Projection.Should().Be(projectionDocument);
            operation.ResultSerializer.Should().BeOfType<FindAndModifyValueDeserializer<BsonDocument>>();
            operation.ReturnDocument.Should().Be((Core.Operations.ReturnDocument)returnDocument);
            operation.Sort.Should().Be(sortDocument);
            operation.Update.Should().Be(updateDocument);
            operation.WriteConcern.Should().BeSameAs(subject.Settings.WriteConcern);
        }

        [Theory]
        [ParameterAttributeData]
        public void FindOneAndUpdate_with_Projection_As_should_execute_correctly(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<A>();
            var session = CreateSession(usingSession);
            var filterDefinition = Builders<A>.Filter.Empty;
            var updateDefinition = Builders<A>.Update.Inc(x => x.PropA, 1);
            var options = new FindOneAndUpdateOptions<A, BsonDocument>
            {
                Projection = Builders<A>.Projection.As<BsonDocument>()
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.FindOneAndUpdateAsync(session, filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndUpdate(session, filterDefinition, updateDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.FindOneAndUpdateAsync(filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.FindOneAndUpdate(filterDefinition, updateDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<FindOneAndUpdateOperation<BsonDocument>>().Subject;
            operation.Projection.Should().BeNull();
            operation.ResultSerializer.Should().BeOfType<FindAndModifyValueDeserializer<BsonDocument>>();
        }

        [Theory]
        [ParameterAttributeData]
        public void Indexes_CreateOne_should_execute_a_CreateIndexesOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var writeConcern = new WriteConcern(1);
            var subject = CreateSubject<BsonDocument>().WithWriteConcern(writeConcern);
            var session = CreateSession(usingSession);
            var keysDocument = new BsonDocument("x", 1);
            var keysDefinition = (IndexKeysDefinition<BsonDocument>)keysDocument;
            var partialFilterDocument = BsonDocument.Parse("{ x : { $gt : 0 } }");
            var partialFilterDefinition = (FilterDefinition<BsonDocument>)partialFilterDocument;
            var weights = new BsonDocument("y", 1);
            var storageEngine = new BsonDocument("awesome", true);
            var options = new CreateIndexOptions<BsonDocument>
            {
                Background = true,
                Bits = 10,
                BucketSize = 20,
                Collation = new Collation("en_US"),
                DefaultLanguage = "en",
                ExpireAfter = TimeSpan.FromSeconds(20),
                LanguageOverride = "es",
                Max = 30,
                Min = 40,
                Name = "awesome",
                PartialFilterExpression = partialFilterDefinition,
                Sparse = false,
                SphereIndexVersion = 50,
                StorageEngine = storageEngine,
                TextIndexVersion = 60,
                Unique = true,
                Version = 70,
                Weights = weights
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.Indexes.CreateOneAsync(session, keysDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.CreateOne(session, keysDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.Indexes.CreateOneAsync(keysDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.CreateOne(keysDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<CreateIndexesOperation>().Subject;
            operation.CollectionNamespace.FullName.Should().Be("foo.bar");
            operation.Requests.Count().Should().Be(1);
            operation.WriteConcern.Should().BeSameAs(writeConcern);

            var request = operation.Requests.Single();
            request.AdditionalOptions.Should().BeNull();
            request.Background.Should().Be(options.Background);
            request.Bits.Should().Be(options.Bits);
            request.BucketSize.Should().Be(options.BucketSize);
            request.Collation.Should().BeSameAs(options.Collation);
            request.DefaultLanguage.Should().Be(options.DefaultLanguage);
            request.ExpireAfter.Should().Be(options.ExpireAfter);
            request.Keys.Should().Be(keysDocument);
            request.LanguageOverride.Should().Be(options.LanguageOverride);
            request.Max.Should().Be(options.Max);
            request.Min.Should().Be(options.Min);
            request.Name.Should().Be(options.Name);
            request.PartialFilterExpression.Should().Be(partialFilterDocument);
            request.Sparse.Should().Be(options.Sparse);
            request.SphereIndexVersion.Should().Be(options.SphereIndexVersion);
            request.StorageEngine.Should().Be(options.StorageEngine);
            request.TextIndexVersion.Should().Be(options.TextIndexVersion);
            request.Unique.Should().Be(options.Unique);
            request.Version.Should().Be(options.Version);
            request.Weights.Should().Be(options.Weights);
            request.GetIndexName().Should().Be(options.Name);
        }

        [Theory]
        [ParameterAttributeData]
        public void Indexes_CreateMany_should_execute_a_CreateIndexesOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var writeConcern = new WriteConcern(1);
            var subject = CreateSubject<BsonDocument>().WithWriteConcern(writeConcern);
            var session = CreateSession(usingSession);
            var keysDocument1 = new BsonDocument("x", 1);
            var keysDocument2 = new BsonDocument("z", 1);
            var keysDefinition1 = (IndexKeysDefinition<BsonDocument>)keysDocument1;
            var keysDefinition2 = (IndexKeysDefinition<BsonDocument>)keysDocument2;
            var partialFilterDocument = BsonDocument.Parse("{ x : { $gt : 0 } }");
            var partialFilterDefinition = (FilterDefinition<BsonDocument>)partialFilterDocument;
            var weights = new BsonDocument("y", 1);
            var storageEngine = new BsonDocument("awesome", true);
            var options = new CreateIndexOptions<BsonDocument>
            {
                Background = true,
                Bits = 10,
                BucketSize = 20,
                Collation = new Collation("en_US"),
                DefaultLanguage = "en",
                ExpireAfter = TimeSpan.FromSeconds(20),
                LanguageOverride = "es",
                Max = 30,
                Min = 40,
                Name = "awesome",
                PartialFilterExpression = partialFilterDefinition,
                Sparse = false,
                SphereIndexVersion = 50,
                StorageEngine = storageEngine,
                TextIndexVersion = 60,
                Unique = true,
                Version = 70,
                Weights = weights
            };
            var model1 = new CreateIndexModel<BsonDocument>(keysDocument1, options);
            var model2 = new CreateIndexModel<BsonDocument>(keysDocument2);
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.Indexes.CreateManyAsync(session, new[] { model1, model2 }, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.CreateMany(session, new[] { model1, model2 }, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.Indexes.CreateManyAsync(new[] { model1, model2 }, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.CreateMany(new[] { model1, model2 }, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<CreateIndexesOperation>().Subject;
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Requests.Count().Should().Be(2);
            operation.WriteConcern.Should().BeSameAs(writeConcern);

            var request1 = operation.Requests.ElementAt(0);
            request1.AdditionalOptions.Should().BeNull();
            request1.Background.Should().Be(options.Background);
            request1.Bits.Should().Be(options.Bits);
            request1.BucketSize.Should().Be(options.BucketSize);
            request1.Collation.Should().BeSameAs(options.Collation);
            request1.DefaultLanguage.Should().Be(options.DefaultLanguage);
            request1.ExpireAfter.Should().Be(options.ExpireAfter);
            request1.Keys.Should().Be(keysDocument1);
            request1.LanguageOverride.Should().Be(options.LanguageOverride);
            request1.Max.Should().Be(options.Max);
            request1.Min.Should().Be(options.Min);
            request1.Name.Should().Be(options.Name);
            request1.PartialFilterExpression.Should().Be(partialFilterDocument);
            request1.Sparse.Should().Be(options.Sparse);
            request1.SphereIndexVersion.Should().Be(options.SphereIndexVersion);
            request1.StorageEngine.Should().Be(storageEngine);
            request1.TextIndexVersion.Should().Be(options.TextIndexVersion);
            request1.Unique.Should().Be(options.Unique);
            request1.Version.Should().Be(options.Version);
            request1.Weights.Should().Be(weights);
            request1.GetIndexName().Should().Be(options.Name);

            var request2 = operation.Requests.ElementAt(1);
            request2.AdditionalOptions.Should().BeNull();
            request2.Background.Should().NotHaveValue();
            request2.Bits.Should().NotHaveValue();
            request2.BucketSize.Should().NotHaveValue();
            request2.Collation.Should().BeNull();
            request2.DefaultLanguage.Should().BeNull();
            request2.ExpireAfter.Should().NotHaveValue();
            request2.Keys.Should().Be(keysDocument2);
            request2.LanguageOverride.Should().BeNull();
            request2.Max.Should().NotHaveValue();
            request2.Min.Should().NotHaveValue(); ;
            request2.Name.Should().BeNull();
            request2.PartialFilterExpression.Should().BeNull();
            request2.Sparse.Should().NotHaveValue(); ;
            request2.SphereIndexVersion.Should().NotHaveValue();
            request2.StorageEngine.Should().BeNull();
            request2.TextIndexVersion.Should().NotHaveValue();
            request2.Unique.Should().NotHaveValue();
            request2.Version.Should().NotHaveValue();
            request2.Weights.Should().BeNull();
            request2.GetIndexName().Should().Be("z_1");
        }

        [Theory]
        [ParameterAttributeData]
        public void Indexes_DropAll_should_execute_a_DropIndexOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var writeConcern = new WriteConcern(1);
            var subject = CreateSubject<BsonDocument>().WithWriteConcern(writeConcern);
            var session = CreateSession(usingSession);
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.Indexes.DropAllAsync(session, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.DropAll(session, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.Indexes.DropAllAsync(cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.DropAll(cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<DropIndexOperation>().Subject;
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.IndexName.Should().Be("*");
            operation.WriteConcern.Should().BeSameAs(writeConcern);
        }

        [Theory]
        [ParameterAttributeData]
        public void Indexes_DropOne_should_execute_a_DropIndexOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var writeConcern = new WriteConcern(1);
            var subject = CreateSubject<BsonDocument>().WithWriteConcern(writeConcern);
            var session = CreateSession(usingSession);
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.Indexes.DropOneAsync(session, "name", cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.DropOne(session, "name", cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.Indexes.DropOneAsync("name", cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.DropOne("name", cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<DropIndexOperation>().Subject;
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.IndexName.Should().Be("name");
            operation.WriteConcern.Should().BeSameAs(writeConcern);
        }

        [Theory]
        [ParameterAttributeData]
        public void Indexes_DropOne_should_throw_an_exception_if_an_asterisk_is_used(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var cancellationToken = new CancellationTokenSource().Token;

            Exception exception;
            if (async)
            {
                exception = Record.Exception(() => subject.Indexes.DropOneAsync("*", cancellationToken).GetAwaiter().GetResult());
            }
            else
            {
                exception = Record.Exception(() => subject.Indexes.DropOne("*", cancellationToken));
            }

            var e = exception.Should().BeOfType<ArgumentException>().Subject;
            e.ParamName.Should().Be("name");
        }

        [Theory]
        [ParameterAttributeData]
        public void Indexes_List_should_execute_a_ListIndexesOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.Indexes.ListAsync(session, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.List(session, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.Indexes.ListAsync(cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Indexes.List(cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<BsonDocument>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<ListIndexesOperation>().Subject;
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
        }

        [Theory]
        [ParameterAttributeData]
        public void InsertOne_should_execute_a_BulkMixedOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var document = BsonDocument.Parse("{ _id : 1, a : 1 }");
            var options = new InsertOneOptions();
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new InsertRequest(document) { CorrelationId = 0 };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(1, new[] { processedRequest });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.InsertOneAsync(session, document, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.InsertOne(session, document, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.InsertOneAsync(document, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.InsertOne(document, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifySingleWrite(call, null, true, processedRequest);
        }

        [Theory]
        [ParameterAttributeData]
        public void InsertOne_should_throw_a_WriteException_when_an_error_occurs(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var document = BsonDocument.Parse("{ _id : 1, a : 1 }");
            var options = new InsertOneOptions();
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new InsertRequest(document) { CorrelationId = 0 };
            var operationException = new MongoBulkWriteOperationException(
                _connectionId,
                new BulkWriteOperationResult.Acknowledged(
                    requestCount: 1,
                    matchedCount: 0,
                    deletedCount: 0,
                    insertedCount: 0,
                    modifiedCount: 0,
                    processedRequests: new[] { processedRequest },
                    upserts: new List<BulkWriteOperationUpsert>()),
                new[] { new BulkWriteOperationError(0, 1, "blah", new BsonDocument()) },
                null,
                new List<WriteRequest>());
            _operationExecutor.EnqueueException<BulkWriteOperationResult>(operationException);

            Exception exception;
            if (usingSession)
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.InsertOneAsync(session, document, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.InsertOne(session, document, options, cancellationToken));
                }
            }
            else
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.InsertOneAsync(document, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.InsertOne(document, options, cancellationToken));
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            exception.Should().BeOfType<MongoWriteException>();
        }

        [Theory]
        [ParameterAttributeData]
        public void InsertOne_should_respect_AssignIdOnInsert(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool assignIdOnInsert,
            [Values(false, true)] bool async)
        {
            var settings = new MongoCollectionSettings { AssignIdOnInsert = assignIdOnInsert };
            var subject = CreateSubject<BsonDocument>(settings);
            var session = CreateSession(usingSession);
            var document = BsonDocument.Parse("{ a : 1 }");
            var options = new InsertOneOptions();
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new InsertRequest(document) { CorrelationId = 0 };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(1, new[] { processedRequest });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.InsertOneAsync(session, document, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.InsertOne(session, document, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.InsertOneAsync(document, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.InsertOne(document, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifySingleWrite(call, null, true, processedRequest);

            var operation = call.Operation.Should().BeOfType<BulkMixedWriteOperation>().Subject;
            var requests = operation.Requests.ToList(); // call ToList to force evaluation
            document.Contains("_id").Should().Be(assignIdOnInsert);
        }

        [Theory]
        [ParameterAttributeData]
        public void InsertMany_should_execute_a_BulkMixedOperation(
            [Values(false, true)] bool usingSession,
            [Values(null, false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isOrdered,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var documents = new[]
            {
                BsonDocument.Parse("{ _id : 1, a : 1 }"),
                BsonDocument.Parse("{ _id : 2, a : 2 }")
            };
            var options = new InsertManyOptions
            {
                BypassDocumentValidation = bypassDocumentValidation,
                IsOrdered = isOrdered
            };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequests = new[]
            {
                new InsertRequest(documents[0]) { CorrelationId = 0 },
                new InsertRequest(documents[1]) { CorrelationId = 1 }
            };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(2, processedRequests);
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.InsertManyAsync(session, documents, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.InsertMany(session, documents, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.InsertManyAsync(documents, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.InsertMany(documents, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifyBulkWrite(call, bypassDocumentValidation, isOrdered, processedRequests);
        }

        [Theory]
        [ParameterAttributeData]
        public void InsertMany_should_respect_AssignIdOnInsert(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool assignIdOnInsert,
            [Values(false, true)] bool async)
        {
            var settings = new MongoCollectionSettings { AssignIdOnInsert = assignIdOnInsert };
            var subject = CreateSubject<BsonDocument>(settings);
            var session = CreateSession(usingSession);
            var document = BsonDocument.Parse("{ a : 1 }");
            var options = new InsertManyOptions();
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new InsertRequest(document) { CorrelationId = 0 };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(1, new[] { processedRequest });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.InsertManyAsync(session, new[] { document }, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.InsertMany(session, new[] { document }, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.InsertManyAsync(new[] { document }, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.InsertMany(new[] { document }, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifySingleWrite(call, null, true, processedRequest);

            var operation = (BulkMixedWriteOperation)call.Operation;
            var requests = operation.Requests.ToList(); // call ToList to force evaluation
            document.Contains("_id").Should().Be(assignIdOnInsert);
        }

        [Theory]
        [ParameterAttributeData]
        public void MapReduce_with_inline_output_mode_should_execute_a_MapReduceOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var map = new BsonJavaScript("map");
            var reduce = new BsonJavaScript("reduce");
            var filterDocument = new BsonDocument("filter", 1);
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var sortDocument = new BsonDocument("sort", 1);
            var sortDefinition = (SortDefinition<BsonDocument>)sortDocument;
            var options = new MapReduceOptions<BsonDocument, BsonDocument>
            {
                Collation = new Collation("en_US"),
                Filter = filterDefinition,
                Finalize = new BsonJavaScript("finalizer"),
                JavaScriptMode = true,
                Limit = 10,
                MaxTime = TimeSpan.FromMinutes(2),
                OutputOptions = MapReduceOutputOptions.Inline,
                Scope = new BsonDocument("test", 3),
                Sort = sortDefinition,
                Verbose = true
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.MapReduceAsync(session, map, reduce, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.MapReduce(session, map, reduce, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.MapReduceAsync(map, reduce, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.MapReduce(map, reduce, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<BsonDocument>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<MapReduceOperation<BsonDocument>>().Subject;
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Filter.Should().Be(filterDocument);
            operation.FinalizeFunction.Should().Be(options.Finalize);
            operation.JavaScriptMode.Should().Be(options.JavaScriptMode);
            operation.Limit.Should().Be(options.Limit);
            operation.MapFunction.Should().Be(map);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.ReadConcern.Should().Be(subject.Settings.ReadConcern);
            operation.ReduceFunction.Should().Be(reduce);
            operation.ResultSerializer.Should().Be(BsonDocumentSerializer.Instance);
            operation.Scope.Should().Be(options.Scope);
            operation.Sort.Should().Be(sortDocument);
            operation.Verbose.Should().Be(options.Verbose);
        }

        [Theory]
        [ParameterAttributeData]
        public void MapReduce_with_collection_output_mode_should_execute_a_MapReduceOutputToCollectionOperation(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var writeConcern = new WriteConcern(1);
            var subject = CreateSubject<BsonDocument>().WithWriteConcern(writeConcern);
            var session = CreateSession(usingSession);
            var map = new BsonJavaScript("map");
            var reduce = new BsonJavaScript("reduce");
            var filterDocument = new BsonDocument("filter", 1);
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var sortDocument = new BsonDocument("sort", 1);
            var sortDefinition = (SortDefinition<BsonDocument>)sortDocument;
            var options = new MapReduceOptions<BsonDocument, BsonDocument>
            {
                BypassDocumentValidation = true,
                Collation = new Collation("en_US"),
                Filter = filterDefinition,
                Finalize = new BsonJavaScript("finalizer"),
                JavaScriptMode = true,
                Limit = 10,
                MaxTime = TimeSpan.FromMinutes(2),
                OutputOptions = MapReduceOutputOptions.Replace("awesome", "otherDB", true),
                Scope = new BsonDocument("test", 3),
                Sort = sortDefinition,
                Verbose = true
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.MapReduceAsync(session, map, reduce, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.MapReduce(session, map, reduce, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.MapReduceAsync(map, reduce, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.MapReduce(map, reduce, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BsonDocument>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<MapReduceOutputToCollectionOperation>().Subject;
            operation.BypassDocumentValidation.Should().Be(options.BypassDocumentValidation);
            operation.Collation.Should().BeSameAs(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.Filter.Should().Be(filterDocument);
            operation.FinalizeFunction.Should().Be(options.Finalize);
            operation.JavaScriptMode.Should().Be(options.JavaScriptMode);
            operation.Limit.Should().Be(options.Limit);
            operation.MapFunction.Should().Be(map);
            operation.MaxTime.Should().Be(options.MaxTime);
            operation.NonAtomicOutput.Should().NotHaveValue();
            operation.OutputCollectionNamespace.Should().Be(CollectionNamespace.FromFullName("otherDB.awesome"));
            operation.OutputMode.Should().Be(Core.Operations.MapReduceOutputMode.Replace);
            operation.ReduceFunction.Should().Be(reduce);
            operation.Scope.Should().Be(options.Scope);
            operation.ShardedOutput.Should().Be(true);
            operation.Sort.Should().Be(sortDocument);
            operation.Verbose.Should().Be(options.Verbose);
            operation.WriteConcern.Should().BeSameAs(writeConcern);
        }

        [Theory]
        [ParameterAttributeData]
        public void ReplaceOne_should_execute_a_BulkMixedOperation(
            [Values(false, true)] bool usingSession,
            [Values(null, false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isUpsert,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ a : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var replacement = BsonDocument.Parse("{ a : 2 }");
            var collation = new Collation("en_US");
            var options = new UpdateOptions
            {
                BypassDocumentValidation = bypassDocumentValidation,
                Collation = collation,
                IsUpsert = isUpsert
            };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new UpdateRequest(UpdateType.Replacement, filterDocument, replacement)
            {
                Collation = collation,
                CorrelationId = 0,
                IsUpsert = isUpsert,
                IsMulti = false
            };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(9, new[] { processedRequest });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.ReplaceOneAsync(session, filterDefinition, replacement, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.ReplaceOne(session, filterDefinition, replacement, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.ReplaceOneAsync(filterDefinition, replacement, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.ReplaceOne(filterDefinition, replacement, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifySingleWrite(call, bypassDocumentValidation, true, processedRequest);
        }

        [Theory]
        [ParameterAttributeData]
        public void ReplaceOne_should_throw_a_WriteException_when_an_error_occurs(
            [Values(false, true)] bool usingSession,
            [Values(null, false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isUpsert,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ a : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var replacement = BsonDocument.Parse("{ a : 2 }");
            var collation = new Collation("en_US");
            var options = new UpdateOptions
            {
                Collation = collation,
                BypassDocumentValidation = bypassDocumentValidation,
                IsUpsert = isUpsert
            };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new UpdateRequest(UpdateType.Replacement, filterDocument, replacement)
            {
                Collation = collation,
                CorrelationId = 0,
                IsUpsert = isUpsert,
                IsMulti = false
            };
            var operationException = new MongoBulkWriteOperationException(
                _connectionId,
                new BulkWriteOperationResult.Acknowledged(
                    requestCount: 1,
                    matchedCount: 1,
                    deletedCount: 0,
                    insertedCount: 0,
                    modifiedCount: 0,
                    processedRequests: new[] { processedRequest },
                    upserts: new List<BulkWriteOperationUpsert>()),
                new[] { new BulkWriteOperationError(0, 1, "blah", new BsonDocument()) },
                null,
                new List<WriteRequest>());
            _operationExecutor.EnqueueException<BulkWriteOperationResult>(operationException);
            
            Exception exception;
            if (usingSession)
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.ReplaceOneAsync(session, filterDefinition, replacement, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.ReplaceOne(session, filterDefinition, replacement, options, cancellationToken));
                }
            }
            else
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.ReplaceOneAsync(filterDefinition, replacement, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.ReplaceOne(filterDefinition, replacement, options, cancellationToken));
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            exception.Should().BeOfType<MongoWriteException>();
        }

        [Theory]
        [ParameterAttributeData]
        public void UpdateMany_should_execute_a_BulkMixedOperation(
            [Values(false, true)] bool usingSession,
            [Values(null, false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isUpsert,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ a : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var updateDocument = BsonDocument.Parse("{ $set : { a : 1 } }");
            var updateDefinition = (UpdateDefinition<BsonDocument>)updateDocument;
            var arrayFilterDocument = BsonDocument.Parse("{ b : 1 }");
            var arrayFilterDefinition = (ArrayFilterDefinition<BsonDocument>)arrayFilterDocument;
            var collation = new Collation("en_US");
            var options = new UpdateOptions
            {
                ArrayFilters = new[] { arrayFilterDefinition },
                BypassDocumentValidation = bypassDocumentValidation,
                Collation = collation,
                IsUpsert = isUpsert
            };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new UpdateRequest(UpdateType.Update, filterDocument, updateDocument)
            {
                ArrayFilters = new[] { arrayFilterDocument },
                Collation = collation,
                CorrelationId = 0,
                IsUpsert = isUpsert,
                IsMulti = true
            };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(9, new[] { processedRequest });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.UpdateManyAsync(session, filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.UpdateMany(session, filterDefinition, updateDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.UpdateManyAsync(filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.UpdateMany(filterDefinition, updateDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifySingleWrite(call, bypassDocumentValidation, true, processedRequest);
        }

        [Theory]
        [ParameterAttributeData]
        public void UpdateMany_should_throw_a_WriteException_when_an_error_occurs(
            [Values(false, true)] bool usingSession,
            [Values(null, false,true)] bool? bypassDocumentValidation,
            [Values(false,true)] bool isUpsert,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ a : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var updateDocument = BsonDocument.Parse("{ $set : { a : 1 } }");
            var updateDefinition = (UpdateDefinition<BsonDocument>)updateDocument;
            var arrayFilterDocument = BsonDocument.Parse("{ b : 1 }");
            var arrayFilterDefinition = (ArrayFilterDefinition<BsonDocument>)arrayFilterDocument;
            var collation = new Collation("en_US");
            var updateOptions = new UpdateOptions
            {
                ArrayFilters = new[] { arrayFilterDefinition },
                BypassDocumentValidation = bypassDocumentValidation,
                Collation = collation,
                IsUpsert = isUpsert
            };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new UpdateRequest(UpdateType.Update, filterDocument, updateDocument)
            {
                ArrayFilters = new[] { arrayFilterDocument },
                Collation = collation,
                CorrelationId = 0,
                IsUpsert = isUpsert,
                IsMulti = true
            };
            var operationException = new MongoBulkWriteOperationException(
                _connectionId,
                new BulkWriteOperationResult.Acknowledged(
                    requestCount: 1,
                    matchedCount: 1,
                    deletedCount: 0,
                    insertedCount: 0,
                    modifiedCount: 0,
                    processedRequests: new[] { processedRequest },
                    upserts: new List<BulkWriteOperationUpsert>()),
                new[] { new BulkWriteOperationError(0, 1, "blah", new BsonDocument()) },
                null,
                new List<WriteRequest>());
            _operationExecutor.EnqueueException<BulkWriteOperationResult>(operationException);

            Exception exception;
            if (usingSession)
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.UpdateManyAsync(session, filterDefinition, updateDefinition, updateOptions, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.UpdateMany(session, filterDefinition, updateDefinition, updateOptions, cancellationToken));
                }
            }
            else
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.UpdateManyAsync(filterDefinition, updateDefinition, updateOptions, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.UpdateMany(filterDefinition, updateDefinition, updateOptions, cancellationToken));
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            exception.Should().BeOfType<MongoWriteException>();
        }

        [Theory]
        [ParameterAttributeData]
        public void UpdateOne_should_execute_a_BulkMixedOperation(
            [Values(false, true)] bool usingSession,
            [Values(null, false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isUpsert,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ a : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var updateDocument = BsonDocument.Parse("{ $set : { a : 1 } }");
            var updateDefinition = (UpdateDefinition<BsonDocument>)updateDocument;
            var arrayFilterDocument = BsonDocument.Parse("{ b : 1 }");
            var arrayFilterDefinition = (ArrayFilterDefinition<BsonDocument>)arrayFilterDocument;
            var collation = new Collation("en_US");
            var options = new UpdateOptions
            {
                ArrayFilters = new[] { arrayFilterDefinition },
                BypassDocumentValidation = bypassDocumentValidation,
                Collation = collation,
                IsUpsert = isUpsert
            };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new UpdateRequest(UpdateType.Update, filterDocument, updateDocument)
            {
                ArrayFilters = new[] { arrayFilterDocument },
                Collation = collation,
                CorrelationId = 0,
                IsUpsert = isUpsert,
                IsMulti = false
            };
            var operationResult = new BulkWriteOperationResult.Unacknowledged(9, new[] { processedRequest });
            _operationExecutor.EnqueueResult<BulkWriteOperationResult>(operationResult);

            if (usingSession)
            {
                if (async)
                {
                    subject.UpdateOneAsync(session, filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.UpdateOne(session, filterDefinition, updateDefinition, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.UpdateOneAsync(filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.UpdateOne(filterDefinition, updateDefinition, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            VerifySingleWrite(call, bypassDocumentValidation, true, processedRequest);
        }

        [Theory]
        [ParameterAttributeData]
        public void UpdateOne_should_throw_a_WriteException_when_an_error_occurs(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool? bypassDocumentValidation,
            [Values(false, true)] bool isUpsert,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var filterDocument = BsonDocument.Parse("{ a : 1 }");
            var filterDefinition = (FilterDefinition<BsonDocument>)filterDocument;
            var updateDocument = BsonDocument.Parse("{ $set : { a : 1 } }");
            var updateDefinition = (UpdateDefinition<BsonDocument>)updateDocument;
            var arrayFilterDocument = BsonDocument.Parse("{ b : 1 }");
            var arrayFilterDefinition = (ArrayFilterDefinition<BsonDocument>)arrayFilterDocument;
            var collation = new Collation("en_US");
            var options = new UpdateOptions
            {
                ArrayFilters = new[] { arrayFilterDefinition },
                BypassDocumentValidation = bypassDocumentValidation,
                Collation = collation,
                IsUpsert = isUpsert
            };
            var cancellationToken = new CancellationTokenSource().Token;

            var processedRequest = new UpdateRequest(UpdateType.Update, filterDocument, updateDocument)
            {
                ArrayFilters = new[] { arrayFilterDocument },
                Collation = collation,
                CorrelationId = 0,
                IsUpsert = isUpsert,
                IsMulti = false
            };
            var operationException = new MongoBulkWriteOperationException(
                _connectionId,
                new BulkWriteOperationResult.Acknowledged(
                    requestCount: 1,
                    matchedCount: 1,
                    deletedCount: 0,
                    insertedCount: 0,
                    modifiedCount: 0,
                    processedRequests: new[] { processedRequest },
                    upserts: new List<BulkWriteOperationUpsert>()),
                new[] { new BulkWriteOperationError(0, 1, "blah", new BsonDocument()) },
                null,
                new List<WriteRequest>());
            _operationExecutor.EnqueueException<BulkWriteOperationResult>(operationException);

            Exception exception;
            if (usingSession)
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.UpdateOneAsync(session, filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.UpdateOne(session, filterDefinition, updateDefinition, options, cancellationToken));
                }
            }
            else
            {
                if (async)
                {
                    exception = Record.Exception(() => subject.UpdateOneAsync(filterDefinition, updateDefinition, options, cancellationToken).GetAwaiter().GetResult());
                }
                else
                {
                    exception = Record.Exception(() => subject.UpdateOne(filterDefinition, updateDefinition, options, cancellationToken));
                }
            }

            var call = _operationExecutor.GetWriteCall<BulkWriteOperationResult>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            exception.Should().BeOfType<MongoWriteException>();
        }

        [Theory]
        [ParameterAttributeData]
        public void Watch_should_execute_a_ChangeStreamOperation(
            [Values(false, true)] bool usingSession,
            [Values(null, 1)] int? batchSize,
            [Values(null, "a")] string locale,
            [Values(ChangeStreamFullDocumentOption.Default, ChangeStreamFullDocumentOption.UpdateLookup)] ChangeStreamFullDocumentOption fullDocument,
            [Values(null, 1)] int? maxAwaitTimeMS,
            [Values(null, ReadConcernLevel.Local)] ReadConcernLevel? readConcernLevel,
            [Values(null, "{ a : 1 }")] string resumeAferString,
            [Values(false, true)] bool async)
        {
            var collation = locale == null ? null : new Collation(locale);
            var maxAwaitTime = maxAwaitTimeMS == null ? (TimeSpan?)null : TimeSpan.FromMilliseconds(maxAwaitTimeMS.Value);
            var readConcern = readConcernLevel == null ? null : new ReadConcern(readConcernLevel);
            var resumeAfter = resumeAferString == null ? null : BsonDocument.Parse(resumeAferString);
            var subject = CreateSubject<BsonDocument>();
            if (readConcern != null)
            {
                subject = subject.WithReadConcern(readConcern);
            }
            var session = CreateSession(usingSession);
            var stageDocument = BsonDocument.Parse("{ $match : { operationType : \"insert\" } }");
            var pipeline = (PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>>)(new[] { stageDocument });
            var options = new ChangeStreamOptions
            {
                BatchSize = batchSize,
                Collation = collation,
                FullDocument = fullDocument,
                MaxAwaitTime = maxAwaitTime,
                ResumeAfter = resumeAfter
            };
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.WatchAsync(session, pipeline, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Watch(session, pipeline, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.WatchAsync(pipeline, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Watch(pipeline, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<ChangeStreamDocument<BsonDocument>>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<ChangeStreamOperation<ChangeStreamDocument<BsonDocument>>>().Subject;
            operation.BatchSize.Should().Be(options.BatchSize);
            operation.Collation.Should().Be(options.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.FullDocument.Should().Be(options.FullDocument);
            operation.MaxAwaitTime.Should().Be(options.MaxAwaitTime);
            operation.MessageEncoderSettings.Should().NotBeNull();
            operation.Pipeline.Should().HaveCount(1);
            operation.Pipeline[0].Should().Be(stageDocument);
            operation.ReadConcern.Should().Be(subject.Settings.ReadConcern);
            operation.ResultSerializer.ValueType.Should().Be(typeof(ChangeStreamDocument<BsonDocument>));
            operation.ResumeAfter.Should().Be(options.ResumeAfter);
        }

        [Theory]
        [ParameterAttributeData]
        public void Watch_should_execute_a_ChangeStreamOperation_with_default_options_when_options_is_null(
            [Values(false, true)] bool usingSession,
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            var session = CreateSession(usingSession);
            var stageDocument = BsonDocument.Parse("{ $match : { operationType : \"insert\" } }");
            var pipeline = (PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>>)(new[] { stageDocument });
            ChangeStreamOptions options = null;
            var cancellationToken = new CancellationTokenSource().Token;

            if (usingSession)
            {
                if (async)
                {
                    subject.WatchAsync(session, pipeline, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Watch(session, pipeline, options, cancellationToken);
                }
            }
            else
            {
                if (async)
                {
                    subject.WatchAsync(pipeline, options, cancellationToken).GetAwaiter().GetResult();
                }
                else
                {
                    subject.Watch(pipeline, options, cancellationToken);
                }
            }

            var call = _operationExecutor.GetReadCall<IAsyncCursor<ChangeStreamDocument<BsonDocument>>>();
            VerifySessionAndCancellationToken(call, session, cancellationToken);

            var operation = call.Operation.Should().BeOfType<ChangeStreamOperation<ChangeStreamDocument<BsonDocument>>>().Subject;
            var defaultOptions = new ChangeStreamOptions();
            operation.BatchSize.Should().Be(defaultOptions.BatchSize);
            operation.Collation.Should().Be(defaultOptions.Collation);
            operation.CollectionNamespace.Should().Be(subject.CollectionNamespace);
            operation.FullDocument.Should().Be(defaultOptions.FullDocument);
            operation.MaxAwaitTime.Should().Be(defaultOptions.MaxAwaitTime);
            operation.MessageEncoderSettings.Should().NotBeNull();
            operation.Pipeline.Should().HaveCount(1);
            operation.Pipeline[0].Should().Be(stageDocument);
            operation.ReadConcern.Should().Be(subject.Settings.ReadConcern);
            operation.ResultSerializer.ValueType.Should().Be(typeof(ChangeStreamDocument<BsonDocument>));
            operation.ResumeAfter.Should().Be(defaultOptions.ResumeAfter);
        }

        [Theory]
        [ParameterAttributeData]
        public void Watch_should_throw_when_pipeline_is_null(
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject<BsonDocument>();
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>> pipeline = null;

            Exception exception;
            if (async)
            {
                exception = Record.Exception(() => subject.WatchAsync(pipeline).GetAwaiter().GetResult());
            }
            else
            {
                exception = Record.Exception(() => subject.Watch(pipeline));
            }

            var e = exception.Should().BeOfType<ArgumentNullException>().Subject;
            e.ParamName.Should().Be("pipeline");
        }

        [Fact]
        public void WithReadPreference_should_return_a_new_collection_with_the_read_preference_changed()
        {
            var subject = CreateSubject<BsonDocument>();
            var newSubject = subject.WithReadPreference(ReadPreference.Nearest);
            newSubject.Settings.ReadPreference.Should().Be(ReadPreference.Nearest);
        }

        [Fact]
        public void WithWriteConcern_should_return_a_new_collection_with_the_write_concern_changed()
        {
            var subject = CreateSubject<BsonDocument>();
            var newSubject = subject.WithWriteConcern(WriteConcern.WMajority);
            newSubject.Settings.WriteConcern.Should().Be(WriteConcern.WMajority);
        }

        // private methods
        private IMongoDatabase CreateDatabase()
        {
            var mockClient = new Mock<IMongoClient>();
            var client = mockClient.Object;
            mockClient.SetupGet(m => m.Settings).Returns(new MongoClientSettings());

            var databaseNamespace = new DatabaseNamespace("foo");
            var settings = new MongoDatabaseSettings();
            settings.ApplyDefaultValues(client.Settings);
            var cluster = new Mock<ICluster>().Object;
            return new MongoDatabaseImpl(client, databaseNamespace, settings, cluster, _operationExecutor);
        }

        private IClientSessionHandle CreateSession(bool usingSession)
        {
            if (usingSession)
            {
                var client = new Mock<IMongoClient>().Object;
                var options = new ClientSessionOptions();
                var serverSession = new ServerSession();
                var clientSession = new ClientSession(client, options, serverSession, isImplicit: false);
                return new ClientSessionHandle(clientSession);
            }
            else
            {
                return null;
            }
        }

        private IMongoCollection<TDocument> CreateSubject<TDocument>(MongoCollectionSettings settings = null)
        {
            var database = CreateDatabase();
            settings = settings ?? new MongoCollectionSettings();
            settings.ReadConcern = _readConcern;
            return database.GetCollection<TDocument>("bar", settings);
        }

        private RenderedPipelineDefinition<TOutput> RenderPipeline<TInput, TOutput>(IMongoCollection<TInput> collection, PipelineDefinition<TInput, TOutput> pipeline)
        {
            var inputSerializer = collection.DocumentSerializer;
            var serializerRegistry = BsonSerializer.SerializerRegistry;
            return pipeline.Render(inputSerializer, serializerRegistry);
        }

        private static void VerifyBulkWrite(MockOperationExecutor.WriteCall<BulkWriteOperationResult> call, bool? bypassDocumentValidation, bool isOrdered, WriteRequest[] expectedRequests)
        {
            var operation = call.Operation.Should().BeOfType<BulkMixedWriteOperation>().Subject;
            operation.BypassDocumentValidation.Should().Be(bypassDocumentValidation);
            operation.CollectionNamespace.FullName.Should().Be("foo.bar");
            operation.IsOrdered.Should().Be(isOrdered);

            var actualRequests = operation.Requests.ToList();
            actualRequests.Count.Should().Be(expectedRequests.Length);

            for (int i = 0; i < expectedRequests.Length; i++)
            {
                actualRequests[i].ShouldBeEquivalentTo(expectedRequests[i]);
            }
        }

        private static void VerifySessionAndCancellationToken<TDocument>(MockOperationExecutor.ReadCall<TDocument> call, IClientSessionHandle session, CancellationToken cancellationToken)
        {
            call.CancellationToken.Should().Be(cancellationToken);
            if (session == null)
            {
                call.UsedImplicitSession.Should().BeTrue();
            }
            else
            {
                call.SessionId.Should().Be(session.ServerSession.Id);
            }
        }

        private static void VerifySessionAndCancellationToken<TDocument>(MockOperationExecutor.WriteCall<TDocument> call, IClientSessionHandle session, CancellationToken cancellationToken)
        {
            call.CancellationToken.Should().Be(cancellationToken);
            if (session == null)
            {
                call.UsedImplicitSession.Should().BeTrue();
            }
            else
            {
                call.SessionId.Should().Be(session.ServerSession.Id);
            }
        }

        private static void VerifySingleWrite<TRequest>(MockOperationExecutor.WriteCall<BulkWriteOperationResult> call, bool? bypassDocumentValidation, bool isOrdered, TRequest expectedRequest)
            where TRequest : WriteRequest
        {
            VerifyBulkWrite(call, bypassDocumentValidation, isOrdered, new[] { expectedRequest });
        }

        private class A
        {
            public int PropA = 0;
        }

        private class B : A
        {
            public int PropB = 0;
        }
    }
}
