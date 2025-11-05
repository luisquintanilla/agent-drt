// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.AI;

#if AGENTGATEWAY
using AgentGateway.Responses;
using AgentGateway.Responses.Converters;
using AgentGateway.Responses.Models;

namespace AgentGateway.Conversations;
#else
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
#endif

/// <summary>
/// Provides bidirectional conversion between <see cref="ItemResource"/> and <see cref="ChatMessage"/> types.
/// </summary>
internal static class ItemResourceChatMessageConverter
{
    private static readonly JsonSerializerOptions s_defaultJsonOptions = new();

    /// <summary>
    /// Converts a collection of <see cref="ItemResource"/> objects to <see cref="ChatMessage"/> objects.
    /// </summary>
    /// <param name="itemResources">The item resources to convert.</param>
    /// <param name="jsonSerializerOptions">Optional JSON serializer options.</param>
    /// <returns>A collection of chat messages.</returns>
    public static IEnumerable<ChatMessage> ToChatMessages(
        IEnumerable<ItemResource> itemResources,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        JsonSerializerOptions options = jsonSerializerOptions ?? s_defaultJsonOptions;

        // Group items by message type and consolidate content
        List<AIContent> currentMessageContents = [];
        ChatRole? currentRole = null;

        foreach (ItemResource item in itemResources)
        {
            switch (item)
            {
                case ResponsesAssistantMessageItemResource assistantMessage:
                    // Flush any accumulated content from previous message
                    if (currentRole.HasValue && currentMessageContents.Count > 0)
                    {
                        yield return new ChatMessage(currentRole.Value, currentMessageContents);
                        currentMessageContents = [];
                    }

                    // Start accumulating content for this message
                    currentRole = assistantMessage.Role;
                    foreach (ItemContent content in assistantMessage.Content)
                    {
                        AIContent? aiContent = ItemContentConverter.ToAIContent(content);
                        if (aiContent is not null)
                        {
                            currentMessageContents.Add(aiContent);
                        }
                    }

                    break;

                case ResponsesUserMessageItemResource userMessage:
                    // Flush any accumulated content from previous message
                    if (currentRole.HasValue && currentMessageContents.Count > 0)
                    {
                        yield return new ChatMessage(currentRole.Value, currentMessageContents);
                        currentMessageContents = [];
                    }

                    // Start accumulating content for this message
                    currentRole = userMessage.Role;
                    foreach (ItemContent content in userMessage.Content)
                    {
                        AIContent? aiContent = ItemContentConverter.ToAIContent(content);
                        if (aiContent is not null)
                        {
                            currentMessageContents.Add(aiContent);
                        }
                    }

                    break;

                case ResponsesSystemMessageItemResource systemMessage:
                    // Flush any accumulated content from previous message
                    if (currentRole.HasValue && currentMessageContents.Count > 0)
                    {
                        yield return new ChatMessage(currentRole.Value, currentMessageContents);
                        currentMessageContents = [];
                    }

                    // Start accumulating content for this message
                    currentRole = systemMessage.Role;
                    foreach (ItemContent content in systemMessage.Content)
                    {
                        AIContent? aiContent = ItemContentConverter.ToAIContent(content);
                        if (aiContent is not null)
                        {
                            currentMessageContents.Add(aiContent);
                        }
                    }

                    break;

                case ResponsesDeveloperMessageItemResource developerMessage:
                    // Flush any accumulated content from previous message
                    if (currentRole.HasValue && currentMessageContents.Count > 0)
                    {
                        yield return new ChatMessage(currentRole.Value, currentMessageContents);
                        currentMessageContents = [];
                    }

                    // Start accumulating content for this message
                    currentRole = developerMessage.Role;
                    foreach (ItemContent content in developerMessage.Content)
                    {
                        AIContent? aiContent = ItemContentConverter.ToAIContent(content);
                        if (aiContent is not null)
                        {
                            currentMessageContents.Add(aiContent);
                        }
                    }

                    break;

                case FunctionToolCallItemResource functionCall:
                    // Add function call as content to current or new message
                    if (!currentRole.HasValue)
                    {
                        currentRole = ChatRole.Assistant;
                    }

                    IDictionary<string, object?>? arguments = JsonSerializer.Deserialize(
                        functionCall.Arguments,
                        options.GetTypeInfo(typeof(IDictionary<string, object?>))) as IDictionary<string, object?>;

                    currentMessageContents.Add(new FunctionCallContent(
                        functionCall.CallId,
                        functionCall.Name,
                        arguments));
                    break;

                case FunctionToolCallOutputItemResource functionOutput:
                    // Function outputs are typically in a separate message with tool role
                    if (currentRole.HasValue && currentMessageContents.Count > 0)
                    {
                        yield return new ChatMessage(currentRole.Value, currentMessageContents);
                        currentMessageContents = [];
                        currentRole = null;
                    }

                    yield return new ChatMessage(
                        ChatRole.Tool,
                        [new FunctionResultContent(functionOutput.CallId, functionOutput.Output)]);
                    break;

                // Other item types (reasoning, file search, etc.) are ignored for chat message conversion
                // as they don't have direct ChatMessage equivalents
                default:
                    break;
            }
        }

        // Flush any remaining accumulated content
        if (currentRole.HasValue && currentMessageContents.Count > 0)
        {
            yield return new ChatMessage(currentRole.Value, currentMessageContents);
        }
    }

    /// <summary>
    /// Converts a <see cref="ChatMessage"/> to a collection of <see cref="ItemResource"/> objects.
    /// </summary>
    /// <param name="message">The chat message to convert.</param>
    /// <param name="idGenerator">The ID generator to use for creating IDs.</param>
    /// <param name="jsonSerializerOptions">Optional JSON serializer options.</param>
    /// <returns>A collection of item resources.</returns>
    public static IEnumerable<ItemResource> ToItemResources(
        ChatMessage message,
        IdGenerator idGenerator,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return message.ToItemResource(idGenerator, jsonSerializerOptions ?? s_defaultJsonOptions);
    }
}
