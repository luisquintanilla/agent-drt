// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI.Hosting.OpenAI;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// A Conversations API-backed implementation of <see cref="ChatMessageStore"/> for durable message storage.
/// </summary>
/// <remarks>
/// This implementation stores chat messages using the OpenAI Conversations API exposed by the AgentGateway.
/// Each store instance is associated with a specific conversation identified by a unique conversation ID.
/// Messages are stored as ItemResources in the conversation.
/// This class handles the conversion between ChatMessage and Conversations API types.
/// </remarks>
internal sealed class ConversationsChatMessageStore : ChatMessageStore
{
    private readonly ConversationsApiClient _apiClient;
    private readonly string _conversationId;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationsChatMessageStore"/> class.
    /// </summary>
    /// <param name="apiClient">The Conversations API client.</param>
    /// <param name="conversationId">The unique conversation ID for this message store.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public ConversationsChatMessageStore(
        ConversationsApiClient apiClient,
        string conversationId,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        this._apiClient = apiClient;
        this._conversationId = conversationId;
        this._jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc/>
    public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        List<ChatMessage> messages = [];
        string? after = null;
        bool hasMore = true;

        // List all items in the conversation in ascending order (oldest first)
        // Loop until we've retrieved all pages of messages
        while (hasMore)
        {
            ListResponse<ItemResource> response = await this._apiClient
                .ListItemsAsync(this._conversationId, "asc", 100, after, cancellationToken)
                .ConfigureAwait(false);

            // Convert ItemResources to ChatMessages using the converter
            messages.AddRange(ItemResourceChatMessageConverter.ToChatMessages(response.Data, this._jsonOptions));

            // Update pagination state
            hasMore = response.HasMore;
            after = response.LastId;
        }

        return messages;
    }

    /// <inheritdoc/>
    public override async Task AddMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        List<ChatMessage> messageList = messages.ToList();

        if (messageList.Count == 0)
        {
            return;
        }

        // Ensure the conversation exists - create it if it doesn't
        await this.EnsureConversationExistsAsync(cancellationToken).ConfigureAwait(false);

        // Convert ChatMessages to ItemParams
        List<ItemParam> itemParams = messageList.SelectMany(ToItemParams).ToList();

        // Add the items to the conversation via the API client
        await this._apiClient
            .AddItemsAsync(this._conversationId, itemParams, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a ChatMessage to ItemParam objects (input models without IDs).
    /// This is useful for creating items in the Conversations API.
    /// Filters out events that don't map well to ItemParams (e.g., messages with no convertible content).
    /// </summary>
    /// <param name="message">The chat message to convert.</param>
    /// <returns>An enumerable of ItemParam objects.</returns>
    private static IEnumerable<ItemParam> ToItemParams(ChatMessage message)
    {
        // Separate function call/result contents from regular message contents
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent functionCallContent:
                    yield return new FunctionToolCallItemParam
                    {
                        CallId = functionCallContent.CallId,
                        Name = functionCallContent.Name,
                        Arguments = JsonSerializer.Serialize(
                            functionCallContent.Arguments,
                            OpenAIHostingJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>)))
                    };
                    break;

                case FunctionResultContent functionResultContent:
                    string output = functionResultContent.Exception is not null
                        ? $"{functionResultContent.Exception.GetType().Name}(\"{functionResultContent.Exception.Message}\")"
                        : $"{functionResultContent.Result?.ToString() ?? "(null)"}";
                    yield return new FunctionToolCallOutputItemParam
                    {
                        CallId = functionResultContent.CallId,
                        Output = output
                    };
                    break;
            }
        }

        // Convert regular message contents
        List<ItemContent> regularContents = [];
        foreach (AIContent content in message.Contents)
        {
            if (content is not FunctionCallContent and not FunctionResultContent &&
                ItemContentConverter.ToItemContent(content) is { } itemContent)
            {
                regularContents.Add(itemContent);
            }
        }

        // Only create a message item if we have convertible contents
        // This filters out messages that contain only non-convertible content (e.g., UsageContent)
        if (regularContents.Count > 0)
        {
            InputMessageContent messageContent = InputMessageContent.FromContents(regularContents);

            if (message.Role == ChatRole.User)
            {
                yield return new ResponsesUserMessageItemParam { Content = messageContent };
            }
            else if (message.Role == ChatRole.Assistant)
            {
                yield return new ResponsesAssistantMessageItemParam { Content = messageContent };
            }
            else if (message.Role == ChatRole.System)
            {
                yield return new ResponsesSystemMessageItemParam { Content = messageContent };
            }
            else if (string.Equals(message.Role.Value, "developer", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ResponsesDeveloperMessageItemParam { Content = messageContent };
            }
            else
            {
                yield return new ResponsesUserMessageItemParam { Content = messageContent };
            }
        }
    }

    /// <inheritdoc/>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        StoreState state = new()
        {
            ConversationId = this._conversationId
        };

        return JsonSerializer.SerializeToElement(state, jsonSerializerOptions ?? this._jsonOptions);
    }

    /// <summary>
    /// Ensures the conversation exists, creating it if necessary.
    /// </summary>
    private async Task EnsureConversationExistsAsync(CancellationToken cancellationToken)
    {
        bool exists = await this._apiClient
            .ConversationExistsAsync(this._conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            await this._apiClient
                .CreateConversationAsync(this._conversationId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal sealed class StoreState
    {
        public string ConversationId { get; set; } = string.Empty;
    }
}
