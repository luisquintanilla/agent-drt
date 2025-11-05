// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentGateway.Conversations.Models;
using AgentGateway.Models;
using AgentGateway.Responses.Models;
using Orleans;

namespace AgentGateway.Conversations;

/// <summary>
/// Orleans-backed implementation of conversation storage.
/// This implementation provides persistent, distributed storage for conversations and messages.
/// </summary>
internal sealed class OrleansConversationStorage(IGrainFactory grainFactory) : IConversationStorage
{
    public async Task<Conversation> CreateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversation.Id);
        return await grain.CreateAsync(conversation, cancellationToken);
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.GetAsync(cancellationToken);
    }

    public async Task<Conversation?> UpdateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversation.Id);
        return await grain.UpdateAsync(conversation, cancellationToken);
    }

    public async Task<bool> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.DeleteAsync(cancellationToken);
    }

    public async Task<ItemResource> AddItemAsync(string conversationId, ItemResource item, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.AddItemAsync(item, cancellationToken);
    }

    public async Task AddItemsAsync(string conversationId, IEnumerable<ItemResource> items, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        await grain.AppendItemsAsync([.. items], afterItemId: null, cancellationToken);
    }

    public async Task<ItemResource?> GetItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.GetItemAsync(itemId, cancellationToken);
    }

    public async Task<ListResponse<ItemResource>> ListItemsAsync(
        string conversationId,
        int? limit = null,
        SortOrder? order = null,
        string? after = null,
        CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.ListItemsAsync(limit, order, after, cancellationToken);
    }

    public async Task<bool> DeleteItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.DeleteItemAsync(itemId, cancellationToken);
    }
}
