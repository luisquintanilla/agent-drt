// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// A client for interacting with the OpenAI Conversations API.
/// </summary>
/// <remarks>
/// This client handles HTTP communication with the Conversations API exposed by the AgentGateway.
/// It provides methods for creating conversations, listing items, and adding items to conversations.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "This class is instantiated by dependency injection as a hosted service")]
internal sealed class ConversationsApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationsApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client configured to call the AgentGateway Conversations API.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public ConversationsApiClient(
        HttpClient httpClient,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        this._httpClient = httpClient;
        this._jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Retrieves a conversation by ID.
    /// </summary>
    /// <param name="conversationId">The unique conversation ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The conversation if found; otherwise, null.</returns>
    public async Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        try
        {
            Uri requestUri = new($"/v1/conversations/{Uri.EscapeDataString(conversationId)}", UriKind.Relative);

            HttpResponseMessage response = await this._httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<Conversation>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get conversation '{conversationId}'.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to deserialize conversation '{conversationId}'.", ex);
        }
    }

    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    /// <param name="request">The creation request details.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created conversation.</returns>
    public async Task<Conversation> CreateConversationAsync(CreateConversationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            HttpResponseMessage response = await this._httpClient.PostAsJsonAsync(
                "/v1/conversations",
                request,
                this._jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var conversation = await response.Content.ReadFromJsonAsync<Conversation>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
            return conversation ?? throw new InvalidOperationException("Failed to deserialize created conversation.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Failed to create conversation.", ex);
        }
    }

    /// <summary>
    /// Updates a conversation.
    /// </summary>
    /// <param name="conversationId">The unique conversation ID.</param>
    /// <param name="request">The update request details.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated conversation if found; otherwise, null.</returns>
    public async Task<Conversation?> UpdateConversationAsync(string conversationId, UpdateConversationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            Uri requestUri = new($"/v1/conversations/{Uri.EscapeDataString(conversationId)}", UriKind.Relative);

            HttpResponseMessage response = await this._httpClient.PostAsJsonAsync(
                requestUri,
                request,
                this._jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<Conversation>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to update conversation '{conversationId}'.", ex);
        }
    }

    /// <summary>
    /// Deletes a conversation.
    /// </summary>
    /// <param name="conversationId">The unique conversation ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the conversation was deleted; otherwise, false.</returns>
    public async Task<bool> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        try
        {
            Uri requestUri = new($"/v1/conversations/{Uri.EscapeDataString(conversationId)}", UriKind.Relative);

            HttpResponseMessage response = await this._httpClient.DeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();

            return true;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to delete conversation '{conversationId}'.", ex);
        }
    }

    /// <summary>
    /// Lists all items in a conversation.
    /// </summary>
    /// <param name="conversationId">The unique conversation ID.</param>
    /// <param name="order">The order in which to return items (asc or desc).</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <param name="after">An item ID to list items after, used in pagination.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list response with items and pagination info, or an empty response if the conversation doesn't exist.</returns>
    public async Task<ListResponse<ItemResource>> ListItemsAsync(
        string conversationId,
        string order = "asc",
        int limit = 100,
        string? after = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        try
        {
            string afterParam = string.IsNullOrEmpty(after) ? string.Empty : $"&after={Uri.EscapeDataString(after)}";
            Uri requestUri = new($"/v1/conversations/{Uri.EscapeDataString(conversationId)}/items?order={order}&limit={limit}{afterParam}", UriKind.Relative);

            HttpResponseMessage response = await this._httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Conversation doesn't exist yet, return empty response
                return new ListResponse<ItemResource>
                {
                    Data = [],
                    HasMore = false,
                    FirstId = null,
                    LastId = null
                };
            }

            response.EnsureSuccessStatusCode();

            ListResponse<ItemResource>? listResponse = await response.Content
                .ReadFromJsonAsync<ListResponse<ItemResource>>(this._jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return listResponse ?? new ListResponse<ItemResource>
            {
                Data = [],
                HasMore = false,
                FirstId = null,
                LastId = null
            };
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to list items for conversation '{conversationId}'.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to deserialize items for conversation '{conversationId}'.", ex);
        }
    }

    /// <summary>
    /// Adds items to a conversation.
    /// </summary>
    /// <param name="conversationId">The unique conversation ID.</param>
    /// <param name="items">The items to add to the conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list of added items.</returns>
    public async Task<ListResponse<ItemResource>> AddItemsAsync(
        string conversationId,
        IEnumerable<ItemParam> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items.ToList();

        if (itemList.Count == 0)
        {
            return new ListResponse<ItemResource>
            {
                Data = [],
                HasMore = false
            };
        }

        try
        {
            CreateItemsRequest createRequest = new()
            {
                Items = itemList
            };

            Uri requestUri = new($"/v1/conversations/{Uri.EscapeDataString(conversationId)}/items", UriKind.Relative);

            HttpResponseMessage response = await this._httpClient.PostAsJsonAsync(
                requestUri,
                createRequest,
                this._jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ListResponse<ItemResource>>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
            return result ?? new ListResponse<ItemResource>
            {
                Data = [],
                HasMore = false
            };
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to add items to conversation '{conversationId}'.", ex);
        }
    }

    /// <summary>
    /// Retrieves a specific item from a conversation.
    /// </summary>
    /// <param name="conversationId">The unique conversation ID.</param>
    /// <param name="itemId">The unique item ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The item if found; otherwise, null.</returns>
    public async Task<ItemResource?> GetItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        try
        {
            Uri requestUri = new($"/v1/conversations/{Uri.EscapeDataString(conversationId)}/items/{Uri.EscapeDataString(itemId)}", UriKind.Relative);

            HttpResponseMessage response = await this._httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ItemResource>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get item '{itemId}' in conversation '{conversationId}'.", ex);
        }
    }

    /// <summary>
    /// Deletes a specific item from a conversation.
    /// </summary>
    /// <param name="conversationId">The unique conversation ID.</param>
    /// <param name="itemId">The unique item ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the item was deleted; otherwise, false.</returns>
    public async Task<bool> DeleteItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        try
        {
            Uri requestUri = new($"/v1/conversations/{Uri.EscapeDataString(conversationId)}/items/{Uri.EscapeDataString(itemId)}", UriKind.Relative);

            HttpResponseMessage response = await this._httpClient.DeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();

            return true;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to delete item '{itemId}' in conversation '{conversationId}'.", ex);
        }
    }
}
