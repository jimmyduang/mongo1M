﻿/* Copyright 2017 MongoDB Inc.
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

using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.TestHelpers.XunitExtensions;
using MongoDB.Driver.Core.Bindings;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.TestHelpers.XunitExtensions;
using MongoDB.Driver.Core.WireProtocol.Messages.Encoders;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MongoDB.Driver.Core.Operations
{
    public class ChangeStreamOperationTests : OperationTestBase
    {
        [Fact]
        public void constructor_should_initialize_instance()
        {
            var collectionNamespace = new CollectionNamespace(new DatabaseNamespace("foo"), "bar");
            var pipeline = new List<BsonDocument> { BsonDocument.Parse("{ $match : { operationType : \"insert\" } }") };
            var resultSerializer = BsonDocumentSerializer.Instance;
            var messageEncoderSettings = new MessageEncoderSettings();

            var subject = new ChangeStreamOperation<BsonDocument>(collectionNamespace, pipeline, resultSerializer, messageEncoderSettings);

            subject.BatchSize.Should().NotHaveValue();
            subject.Collation.Should().BeNull();
            subject.CollectionNamespace.Should().BeSameAs(collectionNamespace);
            subject.FullDocument.Should().Be(ChangeStreamFullDocumentOption.Default);
            subject.MaxAwaitTime.Should().NotHaveValue();
            subject.MessageEncoderSettings.Should().BeSameAs(messageEncoderSettings);
            subject.Pipeline.Should().Equal(pipeline);
            subject.ReadConcern.Should().Be(ReadConcern.Default);
            subject.ResultSerializer.Should().BeSameAs(resultSerializer);
            subject.ResumeAfter.Should().BeNull();
        }

        [Fact]
        public void constructor_should_throw_when_collectionNamespace_is_null()
        {
            CollectionNamespace collectionNamespace = null;
            var pipeline = new List<BsonDocument> { BsonDocument.Parse("{ $match : { operationType : \"insert\" } }") };
            var resultSerializer = BsonDocumentSerializer.Instance;
            var messageEncoderSettings = new MessageEncoderSettings();


            var exception = Record.Exception(() => new ChangeStreamOperation<BsonDocument>(collectionNamespace, pipeline, resultSerializer, messageEncoderSettings));

            var argumentNullException = exception.Should().BeOfType<ArgumentNullException>().Subject;
            argumentNullException.ParamName.Should().Be("collectionNamespace");
        }

        [Fact]
        public void constructor_should_throw_when_pipeline_is_null()
        {
            var collectionNamespace = new CollectionNamespace(new DatabaseNamespace("foo"), "bar");
            var pipeline = new List<BsonDocument> { BsonDocument.Parse("{ $match : { operationType : \"insert\" } }") };
            IBsonSerializer<BsonDocument> resultSerializer = null;
            var messageEncoderSettings = new MessageEncoderSettings();


            var exception = Record.Exception(() => new ChangeStreamOperation<BsonDocument>(collectionNamespace, pipeline, resultSerializer, messageEncoderSettings));

            var argumentNullException = exception.Should().BeOfType<ArgumentNullException>().Subject;
            argumentNullException.ParamName.Should().Be("resultSerializer");
        }

        [Fact]
        public void constructor_should_throw_when_messageEncoderSettings_is_null()
        {
            var collectionNamespace = new CollectionNamespace(new DatabaseNamespace("foo"), "bar");
            var pipeline = new List<BsonDocument> { BsonDocument.Parse("{ $match : { operationType : \"insert\" } }") };
            var resultSerializer = BsonDocumentSerializer.Instance;
            MessageEncoderSettings messageEncoderSettings = null;


            var exception = Record.Exception(() => new ChangeStreamOperation<BsonDocument>(collectionNamespace, pipeline, resultSerializer, messageEncoderSettings));

            var argumentNullException = exception.Should().BeOfType<ArgumentNullException>().Subject;
            argumentNullException.ParamName.Should().Be("messageEncoderSettings");
        }

        [Fact]
        public void constructor_should_throw_when_resultSerializer_is_null()
        {
            var collectionNamespace = new CollectionNamespace(new DatabaseNamespace("foo"), "bar");
            List<BsonDocument> pipeline = null;
            var resultSerializer = BsonDocumentSerializer.Instance;
            var messageEncoderSettings = new MessageEncoderSettings();


            var exception = Record.Exception(() => new ChangeStreamOperation<BsonDocument>(collectionNamespace, pipeline, resultSerializer, messageEncoderSettings));

            var argumentNullException = exception.Should().BeOfType<ArgumentNullException>().Subject;
            argumentNullException.ParamName.Should().Be("pipeline");
        }

        [Theory]
        [ParameterAttributeData]
        public void BatchSize_get_and_set_should_work(
            [Values(null, 1, 2)] int? value)
        {
            var subject = CreateSubject();

            subject.BatchSize = value;
            var result = subject.BatchSize;

            result.Should().Be(value);
        }

        [Theory]
        [ParameterAttributeData]
        public void Collation_get_and_set_should_work(
            [Values(null, "a", "b")] string locale)
        {
            var value = locale == null ? null : new Collation(locale);
            var subject = CreateSubject();

            subject.Collation = value;
            var result = subject.Collation;

            result.Should().Be(value);
        }

        [Theory]
        [ParameterAttributeData]
        public void CollectionNamespace_get_should_work(
            [Values("a", "b")] string collectionName)
        {
            var value = new CollectionNamespace(new DatabaseNamespace("foo"), collectionName);
            var subject = CreateSubject(collectionNamespace: value);

            var result = subject.CollectionNamespace;

            result.Should().Be(value);
        }

        [Theory]
        [ParameterAttributeData]
        public void FullDocument_get_and_set_should_work(
            [Values(ChangeStreamFullDocumentOption.Default, ChangeStreamFullDocumentOption.UpdateLookup)] ChangeStreamFullDocumentOption value)
        {
            var subject = CreateSubject();

            subject.FullDocument = value;
            var result = subject.FullDocument;

            result.Should().Be(value);
        }

        [Theory]
        [ParameterAttributeData]
        public void MaxAwaitTime_get_and_set_should_work(
            [Values(null, 1, 2)] int? maxAwaitTimeMS)
        {
            var value = maxAwaitTimeMS == null ? (TimeSpan?)null : TimeSpan.FromMilliseconds(maxAwaitTimeMS.Value);
            var subject = CreateSubject();

            subject.MaxAwaitTime = value;
            var result = subject.MaxAwaitTime;

            result.Should().Be(value);
        }

        [Fact]
        public void MessageEncoderSettings_get_should_work()
        {
            var value = new MessageEncoderSettings();
            var subject = CreateSubject(messageEncoderSettings: value);

            var result = subject.MessageEncoderSettings;

            result.Should().BeSameAs(value);
        }

        [Fact]
        public void Pipeline_get_should_work()
        {
            var value = new List<BsonDocument> { BsonDocument.Parse("{ $match : { operationType : \"insert\" } }") };
            var subject = CreateSubject(pipeline: value);

            var result = subject.Pipeline;

            result.Should().Equal(value);
        }

        [Theory]
        [ParameterAttributeData]
        public void ReadConcern_get_and_set_should_work(
            [Values(ReadConcernLevel.Local, ReadConcernLevel.Majority)] ReadConcernLevel? level)
        {
            var subject = CreateSubject();
            var value = new ReadConcern(level);

            subject.ReadConcern = value;
            var result = subject.ReadConcern;

            result.Should().Be(value);
        }


        [Fact]
        public void ReadConcern_set_should_throw_when_value_is_null()
        {
            var subject = CreateSubject();

            var exception = Record.Exception(() => subject.ReadConcern = null);

            var argumentNullException = exception.Should().BeOfType<ArgumentNullException>().Subject;
            argumentNullException.ParamName.Should().Be("value");
        }

        [Fact]
        public void ResultSerializer_get_should_work()
        {
            var value = new Mock<IBsonSerializer<BsonDocument>>().Object;
            var subject = CreateSubject(resultSerializer: value);

            var result = subject.ResultSerializer;

            result.Should().Be(value);
        }

        [Theory]
        [ParameterAttributeData]
        public void ResumeAfter_get_and_set_should_work(
            [Values(null, "{ a : 1 }", "{ a : 2 }")] string valueString)
        {
            var subject = CreateSubject();
            var value = valueString == null ? null : BsonDocument.Parse(valueString);

            subject.ResumeAfter = value;
            var result = subject.ResumeAfter;

            result.Should().Be(value);
        }

        [SkippableTheory]
        [ParameterAttributeData]
        public void Execute_should_return_expected_results_for_drop_collection(
            [Values(false, true)] bool async)
        {
            RequireServer.Check().Supports(Feature.ChangeStreamStage).ClusterTypes(ClusterType.ReplicaSet);
            var pipeline = new[] { BsonDocument.Parse("{ $match : { operationType : \"invalidate\" } }") };
            var resultSerializer = new ChangeStreamDocumentSerializer<BsonDocument>(BsonDocumentSerializer.Instance);
            var messageEncoderSettings = new MessageEncoderSettings();
            var subject = new ChangeStreamOperation<ChangeStreamDocument<BsonDocument>>(_collectionNamespace, pipeline, resultSerializer, messageEncoderSettings);
            DropCollection();

            using (var cursor = ExecuteOperation(subject, async))
            using (var enumerator = new AsyncCursorEnumerator<ChangeStreamDocument<BsonDocument>>(cursor, CancellationToken.None))
            {
                Insert("{ _id : 1, x : 1 }");
                DropCollection();

                enumerator.MoveNext().Should().BeTrue();
                var change = enumerator.Current;
                change.OperationType.Should().Be(ChangeStreamOperationType.Invalidate);
                change.CollectionNamespace.Should().BeNull();
                change.DocumentKey.Should().BeNull();
                change.FullDocument.Should().BeNull();
                change.ResumeToken.Should().NotBeNull();
                change.UpdateDescription.Should().BeNull();
            }
        }

        [SkippableTheory]
        [ParameterAttributeData]
        public void Execute_should_return_expected_results_for_deletes(
            [Values(false, true)] bool async)
        {
            RequireServer.Check().Supports(Feature.ChangeStreamStage).ClusterTypes(ClusterType.ReplicaSet);
            var pipeline = new[] { BsonDocument.Parse("{ $match : { operationType : \"delete\" } }") };
            var resultSerializer = new ChangeStreamDocumentSerializer<BsonDocument>(BsonDocumentSerializer.Instance);
            var messageEncoderSettings = new MessageEncoderSettings();
            var subject = new ChangeStreamOperation<ChangeStreamDocument<BsonDocument>>(_collectionNamespace, pipeline, resultSerializer, messageEncoderSettings);
            DropCollection();

            using (var cursor = ExecuteOperation(subject, async))
            using (var enumerator = new AsyncCursorEnumerator<ChangeStreamDocument<BsonDocument>>(cursor, CancellationToken.None))
            {
                Insert("{ _id : 1, x : 1 }");
                Delete("{ _id : 1 }");

                enumerator.MoveNext().Should().BeTrue();
                var change = enumerator.Current;
                change.OperationType.Should().Be(ChangeStreamOperationType.Delete);
                change.CollectionNamespace.Should().Be(_collectionNamespace);
                change.DocumentKey.Should().Be("{ _id : 1 }");
                change.FullDocument.Should().BeNull();
                change.ResumeToken.Should().NotBeNull();
                change.UpdateDescription.Should().BeNull();
            }
        }

        [SkippableTheory]
        [ParameterAttributeData]
        public void Execute_should_return_expected_results_for_inserts(
            [Values(false, true)] bool async)
        {
            RequireServer.Check().Supports(Feature.ChangeStreamStage).ClusterTypes(ClusterType.ReplicaSet, ClusterType.Sharded);
            var pipeline = new[] { BsonDocument.Parse("{ $match : { operationType : \"insert\" } }") };
            var resultSerializer = new ChangeStreamDocumentSerializer<BsonDocument>(BsonDocumentSerializer.Instance);
            var messageEncoderSettings = new MessageEncoderSettings();
            var subject = new ChangeStreamOperation<ChangeStreamDocument<BsonDocument>>(_collectionNamespace, pipeline, resultSerializer, messageEncoderSettings);
            DropCollection();
            Insert("{ _id : 1, x : 1 }");

            using (var cursor = ExecuteOperation(subject, async))
            using (var enumerator = new AsyncCursorEnumerator<ChangeStreamDocument<BsonDocument>>(cursor, CancellationToken.None))
            {
                Update("{ _id : 1 }", "{ $set : { x : 2  } }");
                Insert("{ _id : 2, x : 2 }");

                enumerator.MoveNext().Should().BeTrue();
                var change = enumerator.Current;
                change.OperationType.Should().Be(ChangeStreamOperationType.Insert);
                change.CollectionNamespace.Should().Be(_collectionNamespace);
                change.DocumentKey.Should().Be("{ _id : 2 }");
                change.FullDocument.Should().Be("{ _id : 2, x : 2 }");
                change.ResumeToken.Should().NotBeNull();
                change.UpdateDescription.Should().BeNull();
            }
        }

        [SkippableTheory]
        [ParameterAttributeData]
        public void Execute_should_return_expected_results_for_updates(
            [Values(ChangeStreamFullDocumentOption.Default, ChangeStreamFullDocumentOption.UpdateLookup)] ChangeStreamFullDocumentOption fullDocument,
            [Values(false, true)] bool async)
        {
            RequireServer.Check().Supports(Feature.ChangeStreamStage).ClusterTypes(ClusterType.ReplicaSet);
            var pipeline = new[] { BsonDocument.Parse("{ $match : { operationType : \"update\" } }") };
            var resultSerializer = new ChangeStreamDocumentSerializer<BsonDocument>(BsonDocumentSerializer.Instance);
            var messageEncoderSettings = new MessageEncoderSettings();
            var subject = new ChangeStreamOperation<ChangeStreamDocument<BsonDocument>>(_collectionNamespace, pipeline, resultSerializer, messageEncoderSettings)
            {
                FullDocument = fullDocument
            };
            DropCollection();

            using (var cursor = ExecuteOperation(subject, async))
            using (var enumerator = new AsyncCursorEnumerator<ChangeStreamDocument<BsonDocument>>(cursor, CancellationToken.None))
            {
                Insert("{ _id : 1, x : 1 }");
                Update("{ _id : 1 }", "{ $set : { x : 2  } }");

                enumerator.MoveNext().Should().BeTrue();
                var change = enumerator.Current;
                change.OperationType.Should().Be(ChangeStreamOperationType.Update);
                change.CollectionNamespace.Should().Be(_collectionNamespace);
                change.DocumentKey.Should().Be("{ _id : 1 }");
                change.FullDocument.Should().Be(fullDocument == ChangeStreamFullDocumentOption.Default ? null : "{ _id : 1, x : 2 }");
                change.ResumeToken.Should().NotBeNull();
                change.UpdateDescription.RemovedFields.Should().BeEmpty();
                change.UpdateDescription.UpdatedFields.Should().Be("{ x : 2 }");
            }
        }

        [Theory]
        [ParameterAttributeData]
        public void Execute_should_throw_when_binding_does_not_implement_IReadBindingHandle(
            [Values(false, true)] bool async)
        {
            var subject = CreateSubject();
            var binding = new Mock<IReadBinding>().Object;

            Exception exception;
            if (async)
            {
                exception = Record.Exception(() => subject.ExecuteAsync(binding, CancellationToken.None).GetAwaiter().GetResult());
            }
            else
            {
                exception = Record.Exception(() => subject.Execute(binding, CancellationToken.None));
            }

            var argumentException = exception.Should().BeOfType<ArgumentException>().Subject;
            argumentException.ParamName.Should().Be("binding");
        }

        [Theory]
        [InlineData(null, null, ChangeStreamFullDocumentOption.Default, null, ReadConcernLevel.Local, null, "{ $changeStream : { fullDocument : \"default\" } }")]
        [InlineData(1, null, ChangeStreamFullDocumentOption.Default, null, ReadConcernLevel.Local, null, "{ $changeStream : { fullDocument : \"default\" } }")]
        [InlineData(null, "locale", ChangeStreamFullDocumentOption.Default, null, ReadConcernLevel.Local, null, "{ $changeStream : { fullDocument : \"default\" } }")]
        [InlineData(null, null, ChangeStreamFullDocumentOption.UpdateLookup, null, ReadConcernLevel.Local, null, "{ $changeStream : { fullDocument : \"updateLookup\" } }")]
        [InlineData(null, null, ChangeStreamFullDocumentOption.Default, 1, ReadConcernLevel.Local, null, "{ $changeStream : { fullDocument : \"default\" } }")]
        [InlineData(null, null, ChangeStreamFullDocumentOption.Default, null, ReadConcernLevel.Majority, null, "{ $changeStream : { fullDocument : \"default\" } }")]
        [InlineData(null, null, ChangeStreamFullDocumentOption.Default, null, ReadConcernLevel.Local, "{ a : 1 }", "{ $changeStream: { fullDocument: \"default\", resumeAfter : { a : 1 } } }")]
        [InlineData(1, "locale", ChangeStreamFullDocumentOption.UpdateLookup, 2, ReadConcernLevel.Majority, "{ a : 1 }", "{ $changeStream: { fullDocument: \"updateLookup\", resumeAfter : { a : 1 } } }")]
        public void CreateAggregateOperation_should_return_expected_result(
            int? batchSize,
            string locale,
            ChangeStreamFullDocumentOption fullDocument,
            int? maxAwaitTimeMS,
            ReadConcernLevel level,
            string resumeAferJson,
            string expectedChangeStreamStageJson)
        {
            var collation = locale == null ? null : new Collation(locale);
            var maxAwaitTime = maxAwaitTimeMS == null ? (TimeSpan?)null : TimeSpan.FromMilliseconds(maxAwaitTimeMS.Value);
            var readConcern = new ReadConcern(level);
            var resumeAfter = resumeAferJson == null ? null : BsonDocument.Parse(resumeAferJson);
            var expectedChangeStreamStage = BsonDocument.Parse(expectedChangeStreamStageJson);
            var collectionNamespace = new CollectionNamespace(new DatabaseNamespace("foo"), "bar");
            var pipeline = new List<BsonDocument> { BsonDocument.Parse("{ $match : { operationType : \"insert\" } }") };
            var resultSerializer = BsonDocumentSerializer.Instance;
            var messageEncoderSettings = new MessageEncoderSettings();
            var subject = new ChangeStreamOperation<BsonDocument>(collectionNamespace, pipeline, resultSerializer, messageEncoderSettings)
            {
                BatchSize = batchSize,
                Collation = collation,
                FullDocument = fullDocument,
                MaxAwaitTime = maxAwaitTime,
                ReadConcern = readConcern,
                ResumeAfter = resumeAfter
            };
            var expectedPipeline = new BsonDocument[]
            {
                expectedChangeStreamStage,
                pipeline[0]
            };

            var result = subject.CreateAggregateOperation(resumeAfter);

            result.AllowDiskUse.Should().NotHaveValue();
            result.BatchSize.Should().Be(batchSize);
            result.Collation.Should().Be(collation);
            result.CollectionNamespace.Should().Be(collectionNamespace);
            result.MaxAwaitTime.Should().Be(maxAwaitTime);
            result.MaxTime.Should().NotHaveValue();
            result.MessageEncoderSettings.Should().BeSameAs(messageEncoderSettings);
            result.Pipeline.Should().Equal(expectedPipeline);
            result.ReadConcern.Should().Be(readConcern);
            result.ResultSerializer.Should().Be(RawBsonDocumentSerializer.Instance);
        }

        // private methods
        private ChangeStreamOperation<BsonDocument> CreateSubject(
            CollectionNamespace collectionNamespace = null,
            List<BsonDocument> pipeline = null,
            IBsonSerializer<BsonDocument> resultSerializer = null,
            MessageEncoderSettings messageEncoderSettings = null)
        {
            collectionNamespace = collectionNamespace ?? new CollectionNamespace(new DatabaseNamespace("foo"), "bar");
            pipeline = pipeline ?? new List<BsonDocument> { BsonDocument.Parse("{ $match : { operationType : \"insert\" } }") };
            resultSerializer = resultSerializer ?? BsonDocumentSerializer.Instance;
            messageEncoderSettings = messageEncoderSettings ?? new MessageEncoderSettings();
            return new ChangeStreamOperation<BsonDocument>(collectionNamespace, pipeline, resultSerializer, messageEncoderSettings);
        }
    }

    internal static class ChangeStreamOperationReflector
    {
        public static AggregateOperation<RawBsonDocument> CreateAggregateOperation(
            this ChangeStreamOperation<BsonDocument> subject,
            BsonDocument resumeAfter)
        {
            var methodInfo = typeof(ChangeStreamOperation<BsonDocument>).GetMethod("CreateAggregateOperation", BindingFlags.NonPublic | BindingFlags.Instance);
            return (AggregateOperation<RawBsonDocument>)methodInfo.Invoke(subject, new object[] { resumeAfter });
        }
    }
}
