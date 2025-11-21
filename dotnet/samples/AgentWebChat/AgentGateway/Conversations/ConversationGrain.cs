// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Orleans;
using Orleans.Runtime;

namespace AgentGateway.Conversations;

/// <summary>
/// State for a conversation grain, containing the conversation metadata and its items.
/// </summary>
[GenerateSerializer]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class ConversationState
{
    /// <summary>
    /// The conversation metadata.
    /// </summary>
    [Id(0)]
    public Conversation? Conversation { get; set; }

    /// <summary>
    /// Items (messages) in the conversation, keyed by item ID, maintaining insertion order.
    /// </summary>
    [Id(1)]
    public OrderedDictionary<string, ItemResource> Items { get; set; } = [];
}

/// <summary>
/// Grain interface for managing a single conversation and its messages.
/// </summary>
internal interface IConversationGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    /// <param name="conversation">The conversation to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created conversation.</returns>
    Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the conversation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The conversation if it exists, null otherwise.</returns>
    Task<Conversation?> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Updates the conversation.
    /// </summary>
    /// <param name="conversation">The conversation with updated values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated conversation if found, null otherwise.</returns>
    Task<Conversation?> UpdateAsync(Conversation conversation, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the conversation and all its messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds an item to the conversation.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created item.</returns>
    Task<ItemResource> AddItemAsync(ItemResource item, CancellationToken cancellationToken);

    /// <summary>
    /// Gets an item by ID.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item if found, null otherwise.</returns>
    Task<ItemResource?> GetItemAsync(string itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists items in the conversation with pagination.
    /// </summary>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="order">Sort order.</param>
    /// <param name="after">Return items after this ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list response with items and pagination info.</returns>
    Task<ListResponse<ItemResource>> ListItemsAsync(int? limit, SortOrder? order, string? after, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a specific item from the conversation.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteItemAsync(string itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Appends multiple items to the conversation in an idempotent manner.
    /// </summary>
    /// <param name="items">The items to append.</param>
    /// <param name="afterItemId">The ID of the last item that must exist before appending. If null, items are appended if the conversation is empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of items actually appended (0 if the operation was a duplicate/retry).</returns>
    /// <exception cref="InvalidOperationException">Thrown if the afterItemId doesn't match the last item in the conversation.</exception>
    Task<int> AppendItemsAsync(IReadOnlyList<ItemResource> items, string? afterItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all items in the conversation as an async stream.
    /// </summary>
    /// <param name="order">Sort order for the items.</param>
    /// <returns>An async enumerable of all items in the conversation.</returns>
    IAsyncEnumerable<ItemResource> GetAllItemsAsync(SortOrder order = SortOrder.Ascending);
}

/// <summary>
/// Orleans grain implementation for managing a conversation and its messages.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class ConversationGrain([PersistentState("state")] IPersistentState<ConversationState> conversationState) : Grain, IConversationGrain
{
    private const int DefaultListItemsLimit = 20;

    public async Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        if (conversationState.State.Conversation is not null)
        {
            throw new InvalidOperationException($"Conversation with ID '{conversation.Id}' already exists.");
        }

        conversationState.State.Conversation = conversation;
        await conversationState.WriteStateAsync(cancellationToken);
        return conversation;
    }

    public Task<Conversation?> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(conversationState.State.Conversation);
    }

    public async Task<Conversation?> UpdateAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        if (conversationState.State.Conversation is null)
        {
            return null;
        }

        conversationState.State.Conversation = conversation;
        await conversationState.WriteStateAsync(cancellationToken);
        return conversation;
    }

    public async Task<bool> DeleteAsync(CancellationToken cancellationToken)
    {
        if (conversationState.State.Conversation is null)
        {
            return false;
        }

        await conversationState.ClearStateAsync(cancellationToken);
        return true;
    }

    public async Task<ItemResource> AddItemAsync(ItemResource item, CancellationToken cancellationToken)
    {
        if (conversationState.State.Conversation is null)
        {
            throw new InvalidOperationException("Conversation not found.");
        }

        if (conversationState.State.Items.ContainsKey(item.Id))
        {
            throw new InvalidOperationException($"Item with ID '{item.Id}' already exists in conversation.");
        }

        conversationState.State.Items[item.Id] = item;
        await conversationState.WriteStateAsync(cancellationToken);
        return item;
    }

    public Task<ItemResource?> GetItemAsync(string itemId, CancellationToken cancellationToken)
    {
        conversationState.State.Items.TryGetValue(itemId, out var item);
        return Task.FromResult(item);
    }

    public Task<ListResponse<ItemResource>> ListItemsAsync(int? limit, SortOrder? order, string? after, CancellationToken cancellationToken)
    {
        if (conversationState.State.Conversation is null)
        {
            throw new InvalidOperationException($"Conversation '{this.GetPrimaryKeyString()}' not found.");
        }

        var effectiveLimit = Math.Clamp(limit ?? DefaultListItemsLimit, 1, 100);

        var items = conversationState.State.Items;
        var count = items.Count;
        var effectiveOrder = order ?? SortOrder.Descending;
        var isAscending = effectiveOrder.IsAscending();

        // Determine iteration direction and bounds in insertion order space
        int startIndex = 0;
        int endIndex = count;

        // Handle pagination cursor
        if (!string.IsNullOrEmpty(after))
        {
            var afterIndex = items.IndexOf(after);
            if (afterIndex >= 0)
            {
                // In ascending order: after means skip to next item (afterIndex + 1)
                // In descending order: after means we're going backwards from afterIndex
                if (isAscending)
                {
                    startIndex = afterIndex + 1;
                }
                else
                {
                    endIndex = afterIndex;
                }
            }
        }

        var result = new List<ItemResource>();

        for (int i = 0; i < Math.Min(effectiveLimit + 1, endIndex - startIndex); i++)
        {
            var index = isAscending ? startIndex + i : endIndex - 1 - i;
            if (index >= startIndex && index < endIndex)
            {
                result.Add(items.GetAt(index).Value);
            }
        }

        var hasMore = result.Count > limit;
        if (hasMore)
        {
            result.RemoveAt(result.Count - 1);
        }

        return Task.FromResult(new ListResponse<ItemResource>
        {
            Data = result,
            FirstId = result.Count > 0 ? result[0].Id : null,
            LastId = result.Count > 0 ? result[^1].Id : null,
            HasMore = hasMore
        });
    }

    public async Task<bool> DeleteItemAsync(string itemId, CancellationToken cancellationToken)
    {
        if (conversationState.State.Items.Remove(itemId))
        {
            await conversationState.WriteStateAsync(cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<int> AppendItemsAsync(IReadOnlyList<ItemResource> items, string? afterItemId, CancellationToken cancellationToken)
    {
        if (conversationState.State.Conversation is null)
        {
            throw new InvalidOperationException($"Conversation '{this.GetPrimaryKeyString()}' not found.");
        }

        if (items.Count == 0)
        {
            return 0;
        }

        var currentItems = conversationState.State.Items;
        var lastItemId = currentItems.Count > 0 ? currentItems.GetAt(currentItems.Count - 1).Key : null;

        // Idempotency check: verify the 'after' condition
        if (afterItemId != lastItemId)
        {
            // Check if this is a retry - all items already exist
            if (items.All(m => currentItems.ContainsKey(m.Id)))
            {
                // This appears to be a retry of a successful operation
                return 0;
            }

            // Cannot append items: expected last item to be 'afterItemId' but found 'lastItemId'.
            // This may indicate concurrent modification or a retry after partial success.
            return -1;
        }

        // Append the items
        int appendedCount = 0;
        foreach (var item in items)
        {
            if (!currentItems.ContainsKey(item.Id))
            {
                conversationState.State.Items[item.Id] = item;
                appendedCount++;
            }
        }

        if (appendedCount > 0)
        {
            await conversationState.WriteStateAsync(cancellationToken);
        }

        return appendedCount;
    }

    public async IAsyncEnumerable<ItemResource> GetAllItemsAsync(SortOrder order = SortOrder.Ascending)
    {
        if (conversationState.State.Conversation is null)
        {
            throw new InvalidOperationException($"Conversation '{this.GetPrimaryKeyString()}' not found.");
        }

        var items = conversationState.State.Items;
        var isAscending = order.IsAscending();

        if (isAscending)
        {
            for (int i = 0; i < items.Count; i++)
            {
                yield return items.GetAt(i).Value;
            }
        }
        else
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                yield return items.GetAt(i).Value;
            }
        }

        await Task.CompletedTask;
    }
}
