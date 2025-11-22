// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.Logging;
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
    Task<int> AppendItemsAsync(List<ItemResource> items, string? afterItemId, CancellationToken cancellationToken);

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
internal sealed partial class ConversationGrain([PersistentState("state")] IPersistentState<ConversationState> conversationState, ILogger<ConversationGrain> logger) : Grain, IConversationGrain
{
    private const int DefaultListItemsLimit = 20;

    public async Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        LogCreatingConversation(logger, conversation.Id);

        if (conversationState.State.Conversation is not null)
        {
            LogConversationAlreadyExists(logger, conversation.Id);
            throw new InvalidOperationException($"Conversation with ID '{conversation.Id}' already exists.");
        }

        conversationState.State.Conversation = conversation;
        await conversationState.WriteStateAsync(cancellationToken);

        LogConversationCreated(logger, conversation.Id);
        return conversation;
    }

    public Task<Conversation?> GetAsync(CancellationToken cancellationToken)
    {
        string conversationId = this.GetPrimaryKeyString();
        bool exists = conversationState.State.Conversation is not null;
        LogGettingConversation(logger, conversationId, exists);

        return Task.FromResult(conversationState.State.Conversation);
    }

    public async Task<Conversation?> UpdateAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        LogUpdatingConversation(logger, conversation.Id);

        if (conversationState.State.Conversation is null)
        {
            LogCannotUpdateConversation(logger, conversation.Id);
            return null;
        }

        conversationState.State.Conversation = conversation;
        await conversationState.WriteStateAsync(cancellationToken);

        LogConversationUpdated(logger, conversation.Id);
        return conversation;
    }

    public async Task<bool> DeleteAsync(CancellationToken cancellationToken)
    {
        string conversationId = this.GetPrimaryKeyString();
        LogDeletingConversation(logger, conversationId);

        if (conversationState.State.Conversation is null)
        {
            LogCannotDeleteConversation(logger, conversationId);
            return false;
        }

        await conversationState.ClearStateAsync(cancellationToken);

        LogConversationDeleted(logger, conversationId);
        return true;
    }

    public async Task<ItemResource> AddItemAsync(ItemResource item, CancellationToken cancellationToken)
    {
        string conversationId = this.GetPrimaryKeyString();
        LogAddingItem(logger, item.Id, conversationId);

        if (conversationState.State.Conversation is null)
        {
            LogCannotAddItemConversationNotFound(logger, item.Id, conversationId);
            throw new InvalidOperationException("Conversation not found.");
        }

        if (conversationState.State.Items.ContainsKey(item.Id))
        {
            LogItemAlreadyExists(logger, item.Id, conversationId);
            throw new InvalidOperationException($"Item with ID '{item.Id}' already exists in conversation.");
        }

        conversationState.State.Items[item.Id] = item;
        await conversationState.WriteStateAsync(cancellationToken);

        LogItemAdded(logger, item.Id, conversationId);
        return item;
    }

    public Task<ItemResource?> GetItemAsync(string itemId, CancellationToken cancellationToken)
    {
        string conversationId = this.GetPrimaryKeyString();
        bool found = conversationState.State.Items.TryGetValue(itemId, out ItemResource? item);
        LogGettingItem(logger, itemId, conversationId, found);

        return Task.FromResult(item);
    }

    public Task<ListResponse<ItemResource>> ListItemsAsync(int? limit, SortOrder? order, string? after, CancellationToken cancellationToken)
    {
        string conversationId = this.GetPrimaryKeyString();
        LogListingItems(logger, conversationId, limit, order, after);

        if (conversationState.State.Conversation is null)
        {
            LogCannotListItemsConversationNotFound(logger, conversationId);
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

        LogItemsListed(logger, result.Count, conversationId, hasMore);

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
        string conversationId = this.GetPrimaryKeyString();
        LogDeletingItem(logger, itemId, conversationId);

        if (conversationState.State.Items.Remove(itemId))
        {
            await conversationState.WriteStateAsync(cancellationToken);
            LogItemDeleted(logger, itemId, conversationId);
            return true;
        }

        LogCannotDeleteItem(logger, itemId, conversationId);
        return false;
    }

    public async Task<int> AppendItemsAsync(List<ItemResource> items, string? afterItemId, CancellationToken cancellationToken)
    {
        string conversationId = this.GetPrimaryKeyString();
        LogAppendingItems(logger, items.Count, conversationId, afterItemId);

        if (conversationState.State.Conversation is null)
        {
            LogCannotAppendItemsConversationNotFound(logger, conversationId);
            throw new InvalidOperationException($"Conversation '{this.GetPrimaryKeyString()}' not found.");
        }

        if (items.Count == 0)
        {
            LogNoItemsToAppend(logger, conversationId);
            return 0;
        }

        var currentItems = conversationState.State.Items;
        var lastItemId = currentItems.Count > 0 ? currentItems.GetAt(currentItems.Count - 1).Key : null;

        // Idempotency check: verify the 'after' condition
        if (afterItemId is not null && afterItemId != lastItemId)
        {
            // Check if this is a retry - all items already exist
            if (items.All(m => currentItems.ContainsKey(m.Id)))
            {
                LogAppendRetryDetected(logger, conversationId);

                // This appears to be a retry of a successful operation
                return 0;
            }

            LogAppendConflict(logger, conversationId, afterItemId, lastItemId);

            // Cannot append items: expected last item to be 'afterItemId' but found 'lastItemId'.
            // This may indicate concurrent modification or a retry after partial success.
            return -1;
        }

        // Append the items
        int appendedCount = 0;
        foreach (ItemResource item in items)
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
            LogItemsAppended(logger, appendedCount, conversationId);
        }
        else
        {
            LogNoNewItemsAppended(logger, conversationId);
        }

        return appendedCount;
    }

    public async IAsyncEnumerable<ItemResource> GetAllItemsAsync(SortOrder order = SortOrder.Ascending)
    {
        string conversationId = this.GetPrimaryKeyString();
        LogGettingAllItems(logger, conversationId, order);

        if (conversationState.State.Conversation is null)
        {
            LogCannotGetAllItemsConversationNotFound(logger, conversationId);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating conversation with ID '{ConversationId}'")]
    private static partial void LogCreatingConversation(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation with ID '{ConversationId}' already exists")]
    private static partial void LogConversationAlreadyExists(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully created conversation with ID '{ConversationId}'")]
    private static partial void LogConversationCreated(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Getting conversation with ID '{ConversationId}', exists: {Exists}")]
    private static partial void LogGettingConversation(ILogger logger, string conversationId, bool exists);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating conversation with ID '{ConversationId}'")]
    private static partial void LogUpdatingConversation(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot update conversation with ID '{ConversationId}' - conversation not found")]
    private static partial void LogCannotUpdateConversation(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully updated conversation with ID '{ConversationId}'")]
    private static partial void LogConversationUpdated(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting conversation with ID '{ConversationId}'")]
    private static partial void LogDeletingConversation(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot delete conversation with ID '{ConversationId}' - conversation not found")]
    private static partial void LogCannotDeleteConversation(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully deleted conversation with ID '{ConversationId}'")]
    private static partial void LogConversationDeleted(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adding item '{ItemId}' to conversation '{ConversationId}'")]
    private static partial void LogAddingItem(ILogger logger, string itemId, string conversationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cannot add item '{ItemId}' - conversation '{ConversationId}' not found")]
    private static partial void LogCannotAddItemConversationNotFound(ILogger logger, string itemId, string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Item with ID '{ItemId}' already exists in conversation '{ConversationId}'")]
    private static partial void LogItemAlreadyExists(ILogger logger, string itemId, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully added item '{ItemId}' to conversation '{ConversationId}'")]
    private static partial void LogItemAdded(ILogger logger, string itemId, string conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Getting item '{ItemId}' from conversation '{ConversationId}', found: {Found}")]
    private static partial void LogGettingItem(ILogger logger, string itemId, string conversationId, bool found);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing items for conversation '{ConversationId}' with limit: {Limit}, order: {Order}, after: {After}")]
    private static partial void LogListingItems(ILogger logger, string conversationId, int? limit, SortOrder? order, string? after);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cannot list items - conversation '{ConversationId}' not found")]
    private static partial void LogCannotListItemsConversationNotFound(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} items for conversation '{ConversationId}', hasMore: {HasMore}")]
    private static partial void LogItemsListed(ILogger logger, int count, string conversationId, bool hasMore);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting item '{ItemId}' from conversation '{ConversationId}'")]
    private static partial void LogDeletingItem(ILogger logger, string itemId, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully deleted item '{ItemId}' from conversation '{ConversationId}'")]
    private static partial void LogItemDeleted(ILogger logger, string itemId, string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot delete item '{ItemId}' from conversation '{ConversationId}' - item not found")]
    private static partial void LogCannotDeleteItem(ILogger logger, string itemId, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Appending {Count} items to conversation '{ConversationId}' after item '{AfterItemId}'")]
    private static partial void LogAppendingItems(ILogger logger, int count, string conversationId, string? afterItemId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cannot append items - conversation '{ConversationId}' not found")]
    private static partial void LogCannotAppendItemsConversationNotFound(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No items to append to conversation '{ConversationId}'")]
    private static partial void LogNoItemsToAppend(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Append to conversation '{ConversationId}' appears to be a retry - all items already exist")]
    private static partial void LogAppendRetryDetected(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot append items to conversation '{ConversationId}': expected last item to be '{ExpectedItemId}' but found '{ActualItemId}'")]
    private static partial void LogAppendConflict(ILogger logger, string conversationId, string? expectedItemId, string? actualItemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully appended {AppendedCount} items to conversation '{ConversationId}'")]
    private static partial void LogItemsAppended(ILogger logger, int appendedCount, string conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No new items appended to conversation '{ConversationId}' - items already exist")]
    private static partial void LogNoNewItemsAppended(ILogger logger, string conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Getting all items from conversation '{ConversationId}' with order: {Order}")]
    private static partial void LogGettingAllItems(ILogger logger, string conversationId, SortOrder order);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cannot get all items - conversation '{ConversationId}' not found")]
    private static partial void LogCannotGetAllItemsConversationNotFound(ILogger logger, string conversationId);
}
