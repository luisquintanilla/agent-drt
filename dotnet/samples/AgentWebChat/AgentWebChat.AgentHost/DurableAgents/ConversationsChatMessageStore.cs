// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// A Conversations API-backed implementation of <see cref="IConversationStorage"/> for durable conversation storage.
/// </summary>
/// <remarks>
/// This implementation stores conversations and messages using the OpenAI Conversations API exposed by the AgentGateway.
/// It delegates all operations to the remote Conversations API via HTTP.
/// </remarks>
internal sealed class ConversationsChatMessageStore : IConversationStorage
{
    private readonly ConversationsApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationsChatMessageStore"/> class.
    /// </summary>
    /// <param name="apiClient">The Conversations API client.</param>
    public ConversationsChatMessageStore(ConversationsApiClient apiClient)
    {
        ArgumentNullException.ThrowIfNull(apiClient);

        this._apiClient = apiClient;
    }

    /// <inheritdoc/>
    public async Task<Conversation> CreateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversation.Id);

        await this._apiClient.CreateConversationAsync(conversation.Id, cancellationToken).ConfigureAwait(false);

        return conversation;
    }

    /// <inheritdoc/>
    public async Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        bool exists = await this._apiClient.ConversationExistsAsync(conversationId, cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            return null;
        }

        // Return a basic conversation object since the API doesn't provide full conversation details
        return new Conversation
        {
            Id = conversationId,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Metadata = new Dictionary<string, string>()
        };
    }

    /// <inheritdoc/>
    public async Task<Conversation?> UpdateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversation.Id);

        bool exists = await this._apiClient.ConversationExistsAsync(conversation.Id, cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            return null;
        }

        // The current API client doesn't support updating conversation metadata
        // Return the conversation as-is for now
        return conversation;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        bool exists = await this._apiClient.ConversationExistsAsync(conversationId, cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            return false;
        }

        // The current API client doesn't support deleting conversations
        // This would need to be added to ConversationsApiClient
        throw new NotSupportedException("Deleting conversations is not currently supported by the API client.");
    }

    /// <inheritdoc/>
    public async Task AddItemsAsync(string conversationId, IEnumerable<ItemResource> items, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(items);

        List<ItemResource> itemList = items.ToList();

        if (itemList.Count == 0)
        {
            return;
        }

        // Ensure the conversation exists
        await this.EnsureConversationExistsAsync(conversationId, cancellationToken).ConfigureAwait(false);

        // Convert ItemResources to ItemParams (removing IDs for creation)
        List<ItemParam> itemParams = itemList.SelectMany(ItemResourceToItemParams).ToList();

        // Add the items to the conversation via the API client
        await this._apiClient
            .AddItemsAsync(conversationId, itemParams, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ItemResource?> GetItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        // The current API doesn't support getting a single item directly
        // We need to list all items and find the one we want
        ListResponse<ItemResource> response = await this._apiClient
            .ListItemsAsync(conversationId, "asc", 100, null, cancellationToken)
            .ConfigureAwait(false);

        return response.Data.FirstOrDefault(item => item.Id == itemId);
    }

    /// <inheritdoc/>
    public async Task<ListResponse<ItemResource>> ListItemsAsync(
        string conversationId,
        int? limit = null,
        SortOrder? order = null,
        string? after = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        int effectiveLimit = limit ?? 20;
        string orderStr = order == SortOrder.Ascending ? "asc" : "desc";

        return await this._apiClient
            .ListItemsAsync(conversationId, orderStr, effectiveLimit, after, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        // The current API client doesn't support deleting individual items
        throw new NotSupportedException("Deleting individual items is not currently supported by the API client.");
    }

    /// <summary>
    /// Converts an ItemResource to ItemParam objects.
    /// </summary>
    /// <param name="item">The item resource to convert.</param>
    /// <returns>An enumerable of ItemParam objects.</returns>
    private static IEnumerable<ItemParam> ItemResourceToItemParams(ItemResource item)
    {
        // Convert based on item type
        switch (item)
        {
            case ResponsesUserMessageItemResource userMessage:
                yield return new ResponsesUserMessageItemParam
                {
                    Content = userMessage.Content
                };
                break;

            case ResponsesAssistantMessageItemResource assistantMessage:
                yield return new ResponsesAssistantMessageItemParam
                {
                    Content = assistantMessage.Content
                };
                break;

            case ResponsesSystemMessageItemResource systemMessage:
                yield return new ResponsesSystemMessageItemParam
                {
                    Content = systemMessage.Content
                };
                break;

            case ResponsesDeveloperMessageItemResource developerMessage:
                yield return new ResponsesDeveloperMessageItemParam
                {
                    Content = developerMessage.Content
                };
                break;

            case FunctionToolCallItemResource functionCall:
                yield return new FunctionToolCallItemParam
                {
                    CallId = functionCall.CallId,
                    Name = functionCall.Name,
                    Arguments = functionCall.Arguments
                };
                break;

            case FunctionToolCallOutputItemResource functionOutput:
                yield return new FunctionToolCallOutputItemParam
                {
                    CallId = functionOutput.CallId,
                    Output = functionOutput.Output
                };
                break;

            default:
                // Skip unknown item types
                break;
        }
    }

    /// <summary>
    /// Ensures the conversation exists, creating it if necessary.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task EnsureConversationExistsAsync(string conversationId, CancellationToken cancellationToken)
    {
        bool exists = await this._apiClient
            .ConversationExistsAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            await this._apiClient
                .CreateConversationAsync(conversationId, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
