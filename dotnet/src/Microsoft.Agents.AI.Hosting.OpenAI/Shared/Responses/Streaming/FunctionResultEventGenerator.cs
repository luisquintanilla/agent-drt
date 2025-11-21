// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;

#if AGENTGATEWAY
using AgentGateway.Responses.Models;

namespace AgentGateway.Responses.Streaming;
#else
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses.Streaming;
#endif

/// <summary>
/// A generator for streaming events from function result content.
/// </summary>
public sealed class FunctionResultEventGenerator(
        IdGenerator idGenerator,
        SequenceNumber seq,
        int outputIndex) : StreamingEventGenerator
{
    /// <inheritdoc />
    public override bool IsSupported(AIContent content) => content is FunctionResultContent;

    /// <inheritdoc />
    public override IEnumerable<StreamingResponseEvent> ProcessContent(AIContent content)
    {
        if (content is not FunctionResultContent functionResultContent)
        {
            throw new InvalidOperationException("FunctionResultEventGenerator only supports FunctionResultContent.");
        }

        var item = functionResultContent.ToFunctionToolCallOutputItemResource(idGenerator.GenerateFunctionOutputId());
        yield return new StreamingOutputItemAdded
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            Item = item
        };

        yield return new StreamingOutputItemDone
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            Item = item
        };
    }

    /// <inheritdoc />
    public override IEnumerable<StreamingResponseEvent> Complete() => [];
}
