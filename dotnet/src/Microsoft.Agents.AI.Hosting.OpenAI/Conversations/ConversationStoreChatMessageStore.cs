// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Conversations;

/// <summary>
/// A <see cref="ChatMessageStore"/> implementation that uses <see cref="IConversationStorage"/> for persistence.
/// This store converts between <see cref="ChatMessage"/> and <see cref="ItemResource"/> types
/// to leverage conversation storage capabilities.
/// </summary>
public sealed class ConversationStoreChatMessageStore : ChatMessageStore
{
    private readonly IConversationStorage _conversationStorage;
    private readonly string _conversationId;
    private readonly IdGenerator _idGenerator;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationStoreChatMessageStore"/> class.
    /// </summary>
    /// <param name="conversationStorage">The conversation storage to use for persistence.</param>
    /// <param name="conversationId">The conversation ID to associate messages with.</param>
    /// <param name="idGenerator">The ID generator to use when converting messages to items.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options to use for serialization.</param>
    public ConversationStoreChatMessageStore(
        IConversationStorage conversationStorage,
        string conversationId,
        IdGenerator idGenerator,
        JsonSerializerOptions jsonSerializerOptions)
    {
        _ = Throw.IfNull(conversationStorage);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        _ = Throw.IfNull(idGenerator);
        _ = Throw.IfNull(jsonSerializerOptions);

        this._conversationStorage = conversationStorage;
        this._conversationId = conversationId;
        this._idGenerator = idGenerator;
        this._jsonSerializerOptions = jsonSerializerOptions;
    }

    /// <inheritdoc/>
    public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        // Retrieve all items from the conversation storage
        ListResponse<ItemResource> listResponse = await this._conversationStorage.ListItemsAsync(
            this._conversationId,
            limit: 100,
            order: SortOrder.Ascending,
            after: null,
            cancellationToken).ConfigureAwait(false);

        List<ChatMessage> allMessages = [];

        // Convert items to chat messages
        allMessages.AddRange(ItemResourceChatMessageConverter.ToChatMessages(listResponse.Data, this._jsonSerializerOptions));

        // Handle pagination if there are more items
        while (listResponse.HasMore)
        {
            listResponse = await this._conversationStorage.ListItemsAsync(
                this._conversationId,
                limit: 100,
                order: SortOrder.Ascending,
                after: listResponse.LastId,
                cancellationToken).ConfigureAwait(false);

            allMessages.AddRange(ItemResourceChatMessageConverter.ToChatMessages(listResponse.Data, this._jsonSerializerOptions));
        }

        return allMessages;
    }

    /// <inheritdoc/>
    public override async Task AddMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(messages);

        // Convert chat messages to item resources
        List<ItemResource> items = [];
        foreach (ChatMessage message in messages)
        {
            items.AddRange(message.ToItemResource(this._idGenerator, this._jsonSerializerOptions));
        }

        // Add items to conversation storage
        if (items.Count > 0)
        {
            await this._conversationStorage.AddItemsAsync(this._conversationId, items, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        // Serialize the state of this store
        JsonSerializerOptions options = jsonSerializerOptions ?? this._jsonSerializerOptions;
        var state = new
        {
            ConversationId = this._conversationId
        };

        return JsonSerializer.SerializeToElement(state, options.GetTypeInfo(state.GetType()));
    }
}
