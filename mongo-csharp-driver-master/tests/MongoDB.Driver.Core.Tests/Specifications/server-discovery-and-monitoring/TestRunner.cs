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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.TestHelpers;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Helpers;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.Servers;
using Moq;
using Xunit;

namespace MongoDB.Driver.Specifications.server_discovery_and_monitoring
{
    public class TestRunner
    {
        private ICluster _cluster;
        private IEventSubscriber _eventSubscriber;
        private MockClusterableServerFactory _serverFactory;

        [Theory]
        [ClassData(typeof(TestCaseFactory))]
        public void RunTestDefinition(BsonDocument definition)
        {
            VerifyFields(definition, "description", "path", "phases", "uri");

            _cluster = BuildCluster(definition);
            _cluster.Initialize();

            var phases = definition["phases"].AsBsonArray;
            foreach (BsonDocument phase in phases)
            {
                ApplyPhase(phase);
            }
        }

        private void ApplyPhase(BsonDocument phase)
        {
            VerifyFields(phase, "outcome", "responses");

            var responses = phase["responses"].AsBsonArray;
            foreach (BsonArray response in responses)
            {
                ApplyResponse(response);
            }

            var outcome = (BsonDocument)phase["outcome"];
            VerifyOutcome(outcome);
        }

        private void ApplyResponse(BsonArray response)
        {
            if (response.Count != 2)
            {
                throw new FormatException($"Invalid response count: {response.Count}.");
            }

            var address = response[0].AsString;
            var isMasterDocument = response[1].AsBsonDocument;
            VerifyFields(isMasterDocument, "arbiterOnly", "arbiters", "electionId", "hidden", "hosts", "ismaster", "isreplicaset", "logicalSessionTimeoutMinutes", "maxWireVersion", "me", "minWireVersion", "msg", "ok", "passive", "passives", "primary", "secondary", "setName", "setVersion");

            var endPoint = EndPointHelper.Parse(address);
            var isMasterResult = new IsMasterResult(isMasterDocument);
            var currentServerDescription = _serverFactory.GetServerDescription(endPoint);
            var newServerDescription = currentServerDescription.With(
                canonicalEndPoint: isMasterResult.Me,
                electionId: isMasterResult.ElectionId,
                logicalSessionTimeout: isMasterResult.LogicalSessionTimeout,
                replicaSetConfig: isMasterResult.GetReplicaSetConfig(),
                state: isMasterResult.Wrapped.GetValue("ok", false).ToBoolean() ? ServerState.Connected : ServerState.Disconnected,
                type: isMasterResult.ServerType,
                wireVersionRange: new Range<int>(isMasterResult.MinWireVersion, isMasterResult.MaxWireVersion));

            var currentClusterDescription = _cluster.Description;
            _serverFactory.PublishDescription(newServerDescription);
            SpinWait.SpinUntil(() => !object.ReferenceEquals(_cluster.Description, currentClusterDescription), 100); // sometimes returns false and that's OK
        }

        private void VerifyFields(BsonDocument document, params string[] expectedNames)
        {
            foreach (var name in document.Names)
            {
                if (!expectedNames.Contains(name))
                {
                    throw new FormatException($"Invalid field: \"{name}\".");
                }
            }
        }

        private void VerifyTopology(ICluster cluster, string expectedType)
        {
            switch (expectedType)
            {
                case "Single":
                    cluster.Should().BeOfType<SingleServerCluster>();
                    break;
                case "ReplicaSetWithPrimary":
                    cluster.Should().BeOfType<MultiServerCluster>();
                    cluster.Description.Type.Should().Be(ClusterType.ReplicaSet);
                    cluster.Description.Servers.Should().ContainSingle(x => x.Type == ServerType.ReplicaSetPrimary);
                    break;
                case "ReplicaSetNoPrimary":
                    cluster.Should().BeOfType<MultiServerCluster>();
                    cluster.Description.Type.Should().Be(ClusterType.ReplicaSet);
                    cluster.Description.Servers.Should().NotContain(x => x.Type == ServerType.ReplicaSetPrimary);
                    break;
                case "Sharded":
                    cluster.Should().BeOfType<MultiServerCluster>();
                    cluster.Description.Type.Should().Be(ClusterType.Sharded);
                    break;
                case "Unknown":
                    cluster.Description.Type.Should().Be(ClusterType.Unknown);
                    break;
                default:
                    throw new FormatException($"Invalid topology type: \"{expectedType}\".");
            }
        }

        private void VerifyOutcome(BsonDocument outcome)
        {
            VerifyFields(outcome, "compatible", "logicalSessionTimeoutMinutes", "servers", "setName", "topologyType");

            var expectedTopologyType = (string)outcome["topologyType"];
            VerifyTopology(_cluster, expectedTopologyType);

            var actualDescription = _cluster.Description;

            var actualServers = actualDescription.Servers.Select(x => x.EndPoint);
            var expectedServers = outcome["servers"].AsBsonDocument.Elements.Select(x => new
            {
                EndPoint = EndPointHelper.Parse(x.Name),
                Description = (BsonDocument)x.Value
            });
            actualServers.WithComparer(EndPointHelper.EndPointEqualityComparer).Should().BeEquivalentTo(expectedServers.Select(x => x.EndPoint).WithComparer(EndPointHelper.EndPointEqualityComparer));

            foreach (var actualServer in actualDescription.Servers)
            {
                var expectedServer = expectedServers.Single(x => EndPointHelper.EndPointEqualityComparer.Equals(x.EndPoint, actualServer.EndPoint));
                VerifyServerDescription(actualServer, expectedServer.Description);
            }

            if (outcome.Contains("setName"))
            {
                // TODO: assert something against setName
            }

            if (outcome.Contains("logicalSessionTimeoutMinutes"))
            {
                TimeSpan? expectedLogicalSessionTimeout;
                switch (outcome["logicalSessionTimeoutMinutes"].BsonType)
                {
                    case BsonType.Null:
                        expectedLogicalSessionTimeout = null;
                        break;
                    case BsonType.Int32:
                    case BsonType.Int64:
                        expectedLogicalSessionTimeout = TimeSpan.FromMinutes(outcome["logicalSessionTimeoutMinutes"].ToDouble());
                        break;
                    default:
                        throw new FormatException($"Invalid logicalSessionTimeoutMinutes BSON type: {outcome["setName"].BsonType}.");
                }
                actualDescription.LogicalSessionTimeout.Should().Be(expectedLogicalSessionTimeout);
            }

            if (outcome.Contains("compatible"))
            {
                var expectedIsCompatibleWithDriver = outcome["compatible"].ToBoolean();
                actualDescription.IsCompatibleWithDriver.Should().Be(expectedIsCompatibleWithDriver);
            }
        }

        private void VerifyServerDescription(ServerDescription actualDescription, BsonDocument expectedDescription)
        {
            VerifyFields(expectedDescription, "electionId", "setName", "setVersion", "type");

            var expectedType = (string)expectedDescription["type"];
            switch (expectedType)
            {
                case "RSPrimary":
                    actualDescription.Type.Should().Be(ServerType.ReplicaSetPrimary);
                    break;
                case "RSSecondary":
                    actualDescription.Type.Should().Be(ServerType.ReplicaSetSecondary);
                    break;
                case "RSArbiter":
                    actualDescription.Type.Should().Be(ServerType.ReplicaSetArbiter);
                    break;
                case "RSGhost":
                    actualDescription.Type.Should().Be(ServerType.ReplicaSetGhost);
                    break;
                case "RSOther":
                    actualDescription.Type.Should().Be(ServerType.ReplicaSetOther);
                    break;
                case "Mongos":
                    actualDescription.Type.Should().Be(ServerType.ShardRouter);
                    break;
                case "Standalone":
                    actualDescription.Type.Should().Be(ServerType.Standalone);
                    break;
                default:
                    actualDescription.Type.Should().Be(ServerType.Unknown);
                    break;
            }

            if (expectedDescription.Contains("setName"))
            {
                string expectedSetName;
                switch (expectedDescription["setName"].BsonType)
                {
                    case BsonType.Null: expectedSetName = null; break;
                    case BsonType.String: expectedSetName = expectedDescription["setName"].AsString; ; break;
                    default: throw new FormatException($"Invalid setName BSON type: {expectedDescription["setName"].BsonType}.");
                }
                actualDescription.ReplicaSetConfig?.Name.Should().Be(expectedSetName);
            }

            if (expectedDescription.Contains("setVersion"))
            {
                int? expectedSetVersion;
                switch (expectedDescription["setVersion"].BsonType)
                {
                    case BsonType.Null:
                        expectedSetVersion = null;
                        break;
                    case BsonType.Int32:
                    case BsonType.Int64:
                        expectedSetVersion = expectedDescription["setVersion"].ToInt32();
                        break;
                    default:
                        throw new FormatException($"Invalid setVersion BSON type: {expectedDescription["setVersion"].BsonType}.");
                }
                actualDescription.ReplicaSetConfig?.Version.Should().Be(expectedSetVersion);
            }

            if (expectedDescription.Contains("electionId"))
            {
                ElectionId expectedElectionId;
                switch (expectedDescription["electionId"].BsonType)
                {
                    case BsonType.Null: expectedElectionId = null; break;
                    case BsonType.ObjectId: expectedElectionId = new ElectionId(expectedDescription["electionId"].AsObjectId); break;
                    default: throw new FormatException($"Invalid electionId BSON type: {expectedDescription["electionId"].BsonType}.");
                }
                actualDescription.ElectionId.Should().Be(expectedElectionId);
            }
        }

        private ICluster BuildCluster(BsonDocument definition)
        {
            var connectionString = new ConnectionString((string)definition["uri"]);
            var settings = new ClusterSettings(
                endPoints: Optional.Enumerable(connectionString.Hosts),
                connectionMode: connectionString.Connect,
                replicaSetName: connectionString.ReplicaSet);

            _serverFactory = new MockClusterableServerFactory();
            _eventSubscriber = new Mock<IEventSubscriber>().Object;
            return new ClusterFactory(settings, _serverFactory, _eventSubscriber)
                .CreateCluster();
        }

        private class TestCaseFactory : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
#if NET45
                const string prefix = "MongoDB.Driver.Specifications.server_discovery_and_monitoring.tests.";
                const string monitoringPrefix = "MongoDB.Driver.Specifications.server_discovery_and_monitoring.tests.monitoring.";
#else
                const string prefix = "MongoDB.Driver.Core.Tests.Dotnet.Specifications.server_discovery_and_monitoring.tests.";
                const string monitoringPrefix = "MongoDB.Driver.Core.Tests.Dotnet.Specifications.server_discovery_and_monitoring.tests.monitoring.";
#endif
                var executingAssembly = typeof(TestCaseFactory).GetTypeInfo().Assembly;
                var enumerable = executingAssembly
                    .GetManifestResourceNames()
                    .Where(path => path.StartsWith(prefix) && path.EndsWith(".json"))
                    .Where(path => !path.StartsWith(monitoringPrefix))
                    .Select(path => ReadDefinition(path))
                    .Select(definition => new object[] { definition });
                return enumerable.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private static BsonDocument ReadDefinition(string path)
            {
                var executingAssembly = typeof(TestCaseFactory).GetTypeInfo().Assembly;
                using (var definitionStream = executingAssembly.GetManifestResourceStream(path))
                using (var definitionStringReader = new StreamReader(definitionStream))
                {
                    var definitionString = definitionStringReader.ReadToEnd();
                    var definition = BsonDocument.Parse(definitionString);
                    definition.InsertAt(0, new BsonElement("path", path));
                    return definition;
                }
            }
        }
    }
}
