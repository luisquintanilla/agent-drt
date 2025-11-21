// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

#if AGENTGATEWAY
using AgentGateway.Responses;
using AgentGateway.Responses.Models;
using Microsoft.Agents.AI;

namespace AgentGateway.Conversations;
#else
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
#endif

/// <summary>
/// A <see cref="ChatMessageStore"/> implementation that uses <see cref="IResponseStorage"/> to fetch
/// the chain of prior responses starting from a given response ID.
/// This store walks backwards through the response chain using PreviousResponseId to build
/// the complete message history.
/// </summary>
public sealed class ResponseStoreChatMessageStore : ChatMessageStore
{
    private readonly IResponseStorage _responseStorage;
    private readonly string _responseId;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseStoreChatMessageStore"/> class.
    /// </summary>
    /// <param name="responseStorage">The response storage to use for fetching prior responses.</param>
    /// <param name="responseId">The starting response ID to fetch the chain from.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options to use for serialization.</param>
    public ResponseStoreChatMessageStore(
        IResponseStorage responseStorage,
        string responseId,
        JsonSerializerOptions jsonSerializerOptions)
    {
        _ = Throw.IfNull(responseStorage);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseId);
        _ = Throw.IfNull(jsonSerializerOptions);

        this._responseStorage = responseStorage;
        this._responseId = responseId;
        this._jsonSerializerOptions = jsonSerializerOptions;
    }

    /// <inheritdoc/>
    public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        List<ChatMessage> allMessages = [];
        string? currentResponseId = this._responseId;

        // Walk backwards through the response chain to collect all messages
        List<Response> responseChain = [];
        while (!string.IsNullOrEmpty(currentResponseId))
        {
            StorageResult<Response>? result = await this._responseStorage.GetResponseAsync(currentResponseId, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                break;
            }

            responseChain.Add(result.Value);
            currentResponseId = result.Value.PreviousResponseId;
        }

        // Reverse the chain so we process responses in chronological order (oldest first)
        responseChain.Reverse();

        // Convert all output items from each response to chat messages
        foreach (Response response in responseChain)
        {
            allMessages.AddRange(ItemResourceChatMessageConverter.ToChatMessages(response.Output, this._jsonSerializerOptions));
        }

        return allMessages;
    }

    /// <inheritdoc/>
    public override Task AddMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        // This store is read-only as it's built from the response chain
        // New messages are added through the response execution process, not directly to this store
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        // Serialize the state of this store
        JsonSerializerOptions options = jsonSerializerOptions ?? this._jsonSerializerOptions;
        var state = new
        {
            ResponseId = this._responseId
        };

        return JsonSerializer.SerializeToElement(state, options.GetTypeInfo(state.GetType()));
    }
}
