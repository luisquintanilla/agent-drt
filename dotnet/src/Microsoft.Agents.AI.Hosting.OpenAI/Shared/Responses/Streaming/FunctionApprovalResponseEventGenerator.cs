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
/// A generator for streaming events from function approval response content.
/// This is a non-standard DevUI extension for human-in-the-loop scenarios.
/// </summary>
public sealed class FunctionApprovalResponseEventGenerator(
        IdGenerator idGenerator,
        SequenceNumber seq,
        int outputIndex) : StreamingEventGenerator
{
    /// <inheritdoc />
    public override bool IsSupported(AIContent content) => content is FunctionApprovalResponseContent;

    /// <inheritdoc />
    public override IEnumerable<StreamingResponseEvent> ProcessContent(AIContent content)
    {
        if (content is not FunctionApprovalResponseContent approvalResponse)
        {
            throw new InvalidOperationException("FunctionApprovalResponseEventGenerator only supports FunctionApprovalResponseContent.");
        }

        yield return new StreamingFunctionApprovalResponded
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            RequestId = approvalResponse.Id,
            Approved = approvalResponse.Approved,
            ItemId = idGenerator.GenerateMessageId()
        };
    }

    /// <inheritdoc />
    public override IEnumerable<StreamingResponseEvent> Complete() => [];
}
