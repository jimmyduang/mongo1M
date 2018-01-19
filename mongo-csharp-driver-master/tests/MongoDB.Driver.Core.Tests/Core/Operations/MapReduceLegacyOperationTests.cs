/* Copyright 2013-2017 MongoDB Inc.
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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.TestHelpers.XunitExtensions;
using MongoDB.Driver.Core.Bindings;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.TestHelpers.XunitExtensions;
using Xunit;

namespace MongoDB.Driver.Core.Operations
{
    public class MapReduceLegacyOperationTests : OperationTestBase
    {
        // fields
        private readonly BsonJavaScript _mapFunction = "map";
        private readonly BsonJavaScript _reduceFunction = "reduce";
        private readonly IBsonSerializer<BsonDocument> _resultSerializer = BsonDocumentSerializer.Instance;

        // test methods
        [Fact]
        public void constructor_should_initialize_instance()
        {
            var subject = new MapReduceLegacyOperation(_collectionNamespace, _mapFunction, _reduceFunction, _messageEncoderSettings);

            subject.CollectionNamespace.Should().BeSameAs(_collectionNamespace);
            subject.MapFunction.Should().BeSameAs(_mapFunction);
            subject.MessageEncoderSettings.Should().BeSameAs(_messageEncoderSettings);
            subject.Filter.Should().BeNull();
            subject.ReduceFunction.Should().BeSameAs(_reduceFunction);
        }

        [Fact]
        public void CreateOutputOptions_should_return_expected_result()
        {
            var subject = new MapReduceLegacyOperation(_collectionNamespace, _mapFunction, _reduceFunction, _messageEncoderSettings);
            var subjectReflector = new Reflector(subject);
            var expectedResult = new BsonDocument("inline", 1);

            var result = subjectReflector.CreateOutputOptions();

            result.Should().Be(expectedResult);
        }

        [SkippableTheory]
        [ParameterAttributeData]
        public void Execute_should_return_expected_results(
            [Values(false, true)]
            bool async)
        {
            RequireServer.Check();
            EnsureTestData();

            var mapFunction = "function() { emit(this.x, this.v); }";
            var reduceFunction = "function(key, values) { var sum = 0; for (var i = 0; i < values.length; i++) { sum += values[i]; }; return sum; }";
            var subject = new MapReduceLegacyOperation(_collectionNamespace, mapFunction, reduceFunction, _messageEncoderSettings);
            var expectedResults = new List<BsonDocument>
            {
                new BsonDocument { {"_id", 1 }, { "value", 3 } },
                new BsonDocument { {"_id", 2 }, { "value", 4 } },
            };

            var result = ExecuteOperation(subject, async);

            result["results"].Should().Be(new BsonArray(expectedResults));
        }

        [SkippableTheory]
        [ParameterAttributeData]
        public void Execute_should_send_session_id_when_supported(
            [Values(false, true)] bool async)
        {
            RequireServer.Check();
            EnsureTestData();
            var mapFunction = "function() { emit(this.x, this.v); }";
            var reduceFunction = "function(key, values) { var sum = 0; for (var i = 0; i < values.length; i++) { sum += values[i]; }; return sum; }";
            var subject = new MapReduceLegacyOperation(_collectionNamespace, mapFunction, reduceFunction, _messageEncoderSettings);
            var expectedResults = new List<BsonDocument>
            {
                new BsonDocument { {"_id", 1 }, { "value", 3 } },
                new BsonDocument { {"_id", 2 }, { "value", 4 } },
            };

            VerifySessionIdWasSentWhenSupported(subject, "mapreduce", async);
        }

        [Theory]
        [ParameterAttributeData]
        public void CreateCommand_should_include_read_concern_when_appropriate(
            [Values(null, ReadConcernLevel.Local, ReadConcernLevel.Majority)] ReadConcernLevel? readConcernLevel)
        {
            var readConcern = new ReadConcern(readConcernLevel);
            var mapFunction = "function() { emit(this.x, this.v); }";
            var reduceFunction = "function(key, values) { var sum = 0; for (var i = 0; i < values.length; i++) { sum += values[i]; }; return sum; }";
            var subject = new MapReduceLegacyOperation(_collectionNamespace, mapFunction, reduceFunction, _messageEncoderSettings)
            {
                ReadConcern = readConcern
            };

            var connectionDescription = OperationTestHelper.CreateConnectionDescription(Feature.ReadConcern.FirstSupportedVersion);
            var session = OperationTestHelper.CreateSession();

            var result = subject.CreateCommand(connectionDescription, session);

            if (readConcern.IsServerDefault)
            {
                result.Contains("readConcern").Should().BeFalse();
            }
            else
            {
                result["readConcern"].Should().Be(readConcern.ToBsonDocument());
            }
        }

        [Fact]
        public void CreateCommand_should_throw_when_read_concern_is_not_supported()
        {
            var mapFunction = "function() { emit(this.x, this.v); }";
            var reduceFunction = "function(key, values) { var sum = 0; for (var i = 0; i < values.length; i++) { sum += values[i]; }; return sum; }";
            var subject = new MapReduceLegacyOperation(_collectionNamespace, mapFunction, reduceFunction, _messageEncoderSettings)
            {
                ReadConcern = ReadConcern.Majority
            };

            var connectionDescription = OperationTestHelper.CreateConnectionDescription(Feature.ReadConcern.LastNotSupportedVersion);
            var session = OperationTestHelper.CreateSession();

            Action act = () => subject.CreateCommand(connectionDescription, session);
            act.ShouldThrow<MongoClientException>();
        }

        [Theory]
        [ParameterAttributeData]
        public void CreateCommand_should_include_readConcern_when_using_causal_consistency(
            [Values(null, ReadConcernLevel.Local, ReadConcernLevel.Majority)] ReadConcernLevel? readConcernLevel)
        {
            var readConcern = new ReadConcern(readConcernLevel);
            var mapFunction = "function() { emit(this.x, this.v); }";
            var reduceFunction = "function(key, values) { var sum = 0; for (var i = 0; i < values.length; i++) { sum += values[i]; }; return sum; }";
            var subject = new MapReduceLegacyOperation(_collectionNamespace, mapFunction, reduceFunction, _messageEncoderSettings)
            {
                ReadConcern = readConcern
            };

            var connectionDescription = OperationTestHelper.CreateConnectionDescription(Feature.ReadConcern.FirstSupportedVersion, supportsSessions: true);
            var session = OperationTestHelper.CreateSession(true, new BsonTimestamp(100));

            var result = subject.CreateCommand(connectionDescription, session);

            var expectedReadConcernDocument = readConcern.ToBsonDocument();
            expectedReadConcernDocument["afterClusterTime"] = new BsonTimestamp(100);

            result["readConcern"].Should().Be(expectedReadConcernDocument);
        }

        [Theory]
        [ParameterAttributeData]
        public void Execute_should_throw_when_binding_is_null(
            [Values(false, true)]
            bool async)
        {
            var subject = new MapReduceLegacyOperation(_collectionNamespace, _mapFunction, _reduceFunction, _messageEncoderSettings);
            IReadBinding binding = null;

            Action act = () => ExecuteOperation(subject, binding, async);

            act.ShouldThrow<ArgumentNullException>().And.ParamName.Should().Be("binding");
        }

        // helper methods
        private void EnsureTestData()
        {
            DropCollection();
            Insert(
                new BsonDocument { { "_id", 1 }, { "x", 1 }, { "v", 1 } },
                new BsonDocument { { "_id", 2 }, { "x", 1 }, { "v", 2 } },
                new BsonDocument { { "_id", 3 }, { "x", 2 }, { "v", 4 } });
        }

        // nested types
        private class Reflector
        {
            // fields
            private readonly MapReduceLegacyOperation _instance;

            // constructor
            public Reflector(MapReduceLegacyOperation instance)
            {
                _instance = instance;
            }

            // methods
            public BsonDocument CreateOutputOptions()
            {
                var method = typeof(MapReduceLegacyOperation).GetMethod("CreateOutputOptions", BindingFlags.NonPublic | BindingFlags.Instance);
                return (BsonDocument)method.Invoke(_instance, new object[0]);
            }
        }
    }
}
