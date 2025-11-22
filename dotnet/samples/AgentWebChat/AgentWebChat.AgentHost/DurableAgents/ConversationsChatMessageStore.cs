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

        var request = new CreateConversationRequest
        {
            Metadata = conversation.Metadata ?? []
        };

        return await this._apiClient.CreateConversationAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        return await this._apiClient.GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Conversation?> UpdateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversation.Id);

        var request = new UpdateConversationRequest
        {
            Metadata = conversation.Metadata
        };

        return await this._apiClient.UpdateConversationAsync(conversation.Id, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        return await this._apiClient.DeleteConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);
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

        return await this._apiClient.GetItemAsync(conversationId, itemId, cancellationToken).ConfigureAwait(false);
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
    public async Task<bool> DeleteItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return await this._apiClient.DeleteItemAsync(conversationId, itemId, cancellationToken).ConfigureAwait(false);
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
}
