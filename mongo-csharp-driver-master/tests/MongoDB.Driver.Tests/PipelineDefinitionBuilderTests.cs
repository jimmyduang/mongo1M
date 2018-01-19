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
using System;
using System.Collections.Generic;
using Xunit;

namespace MongoDB.Driver.Tests
{
    public class PipelineDefinitionBuilderTests
    {
        // public methods
        [Theory]
        [InlineData(ChangeStreamFullDocumentOption.Default, null, "{ $changeStream : { fullDocument : \"default\" } }")]
        [InlineData(ChangeStreamFullDocumentOption.UpdateLookup, null, "{ $changeStream : { fullDocument : \"updateLookup\" } }")]
        [InlineData(ChangeStreamFullDocumentOption.Default, "{ a : 1 }", "{ $changeStream : { fullDocument : \"default\", resumeAfter : { a : 1 } } }")]
        [InlineData(ChangeStreamFullDocumentOption.UpdateLookup, "{ a : 1 }", "{ $changeStream : { fullDocument : \"updateLookup\", resumeAfter : { a : 1 } } }")]
        public void ChangeStream_should_add_the_expected_stage(
            ChangeStreamFullDocumentOption fullDocument,
            string resumeAfterString,
            string expectedStage)
        {
            var resumeAfter = resumeAfterString == null ? null : BsonDocument.Parse(resumeAfterString);
            var pipeline = new EmptyPipelineDefinition<BsonDocument>();
            var options = new ChangeStreamStageOptions
            {
                FullDocument = fullDocument,
                ResumeAfter = resumeAfter
            };

            var result = pipeline.ChangeStream(options);

            var stages = RenderStages(result);
            stages.Count.Should().Be(1);
            stages[0].Should().Be(expectedStage);
        }

        [Fact]
        public void ChangeStream_should_add_the_expected_stage_when_options_is_null()
        {
            var pipeline = new EmptyPipelineDefinition<BsonDocument>();
            ChangeStreamStageOptions options = null;

            var result = pipeline.ChangeStream(options);

            var stages = RenderStages(result);
            stages.Count.Should().Be(1);
            stages[0].Should().Be("{ $changeStream : { fullDocument : \"default\" } }");
        }

        [Fact]
        public void ChangeStream_should_throw_when_pipeline_is_null()
        {
            PipelineDefinition<BsonDocument, BsonDocument> pipeline = null;
            ChangeStreamStageOptions options = null;

            var exception = Record.Exception(() => pipeline.ChangeStream(options));

            var argumentNullException = exception.Should().BeOfType<ArgumentNullException>().Subject;
            argumentNullException.ParamName.Should().Be("pipeline");
        }

        // private methods
        private IList<BsonDocument> RenderStages(PipelineDefinition<BsonDocument, ChangeStreamDocument<BsonDocument>> pipeline)
        {
            var renderedPipeline = pipeline.Render(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry);
            return renderedPipeline.Documents;
        }
    }
}
