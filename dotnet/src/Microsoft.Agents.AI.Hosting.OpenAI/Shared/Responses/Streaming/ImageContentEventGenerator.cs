// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;

#if AGENTGATEWAY
using AgentGateway.Responses.Converters;
using AgentGateway.Responses.Models;

namespace AgentGateway.Responses.Streaming;
#else
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses.Streaming;
#endif

/// <summary>
/// A generator for streaming events from image content.
/// </summary>
internal sealed class ImageContentEventGenerator(
        IdGenerator idGenerator,
        SequenceNumber seq,
        int outputIndex) : StreamingEventGenerator
{
    public override bool IsSupported(AIContent content) =>
        (content is UriContent uriContent && uriContent.HasTopLevelMediaType("image")) ||
        (content is DataContent dataContent && dataContent.HasTopLevelMediaType("image"));

    public override IEnumerable<StreamingResponseEvent> ProcessContent(AIContent content)
    {
        if (ItemContentConverter.ToItemContent(content) is not ItemContentInputImage itemContent)
        {
            throw new InvalidOperationException("ImageContentEventGenerator only supports image UriContent and DataContent.");
        }

        var itemId = idGenerator.GenerateMessageId();

        var item = new ResponsesAssistantMessageItemResource
        {
            Id = itemId,
            Status = ResponsesMessageItemResourceStatus.Completed,
            Content = [itemContent]
        };

        yield return new StreamingOutputItemAdded
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            Item = item
        };

        yield return new StreamingContentPartAdded
        {
            SequenceNumber = seq.Increment(),
            ItemId = itemId,
            OutputIndex = outputIndex,
            ContentIndex = 0,
            Part = itemContent
        };

        yield return new StreamingContentPartDone
        {
            SequenceNumber = seq.Increment(),
            ItemId = itemId,
            OutputIndex = outputIndex,
            ContentIndex = 0,
            Part = itemContent
        };

        yield return new StreamingOutputItemDone
        {
            SequenceNumber = seq.Increment(),
            OutputIndex = outputIndex,
            Item = item
        };
    }

    public override IEnumerable<StreamingResponseEvent> Complete() => [];
}
