// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentGateway.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;
using Orleans;

namespace AgentGateway.Responses;

/// <summary>
/// Response executor that uses a local IChatClient to execute responses using ChatClientAgent.
/// This is the original implementation that was embedded in ResponseGrain.
/// </summary>
internal sealed class LocalChatClientResponseExecutor : IResponseExecutor
{
    private readonly IChatClient _chatClient;
    private readonly IGrainFactory _grainFactory;

    public LocalChatClientResponseExecutor(
        IChatClient chatClient,
        IGrainFactory grainFactory)
    {
        this._chatClient = chatClient;
        this._grainFactory = grainFactory;
    }

    public ValueTask<ResponseError?> ValidateRequestAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<ResponseError?>(null);

    public async IAsyncEnumerable<StreamingResponseEvent> ExecuteAsync(
        AgentInvocationContext context,
        CreateResponse request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = new ChatClientAgent(
            this._chatClient,
            instructions: request.Instructions,
            name: "ResponseAgent");

        var (messages, _) = await this.GetThreadAsync(request, agent, context.ConversationId, cancellationToken);

        // Create options with properties from the request
        var chatOptions = new ChatOptions
        {
            ConversationId = request.Conversation?.Id,
            Temperature = (float?)request.Temperature,
            TopP = (float?)request.TopP,
            MaxOutputTokens = request.MaxOutputTokens,
            Instructions = request.Instructions,
            ModelId = request.Model,
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
            AllowBackgroundResponses = request.Background,
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
        };
        var options = new ChatClientAgentRunOptions(chatOptions);

        // Use the extension method to convert streaming updates to streaming response events
        await foreach (var streamingEvent in agent.RunStreamingAsync(messages, agent.GetNewThread(), options, cancellationToken)
            .ToStreamingResponseAsync(request, context, cancellationToken))
        {
            yield return streamingEvent;
        }
    }

    private async Task<(List<ChatMessage> Messages, string? LastMessageId)> GetThreadAsync(
        CreateResponse request,
        ChatClientAgent agent,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();
        string? lastMessageId = null;

        // Per OpenAI API behavior: conversation.id and previous_response_id are mutually exclusive.
        // The previous_response_id determines the conversation thread context - it follows the response chain,
        // not any "active" conversation. Using a response ID from conversation A will continue A's context,
        // even if you just created conversation B.
        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id))
        {
            conversationId = request.Conversation.Id;
            lastMessageId = await this.LoadConversationAsync(messages, conversationId, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(request.PreviousResponseId))
        {
            var previousGrain = this._grainFactory.GetGrain<IResponseGrain>(request.PreviousResponseId);
            var previousResult = await previousGrain.GetWithThreadAsync(cancellationToken);

            if (previousResult is not null)
            {
                var (previousResponse, previousItems) = previousResult.Value;

                // The conversation context follows the response chain.
                // If the previous response was created with a conversation.id, we load that conversation.
                // If the previous response was created with previous_response_id (no conversation), we use its thread directly.
                conversationId = previousResponse.Conversation?.Id;
                if (conversationId is not null)
                {
                    lastMessageId = await this.LoadConversationAsync(messages, conversationId, cancellationToken);
                }
                else
                {
                    // Use the thread from the previous response directly (orphaned response chain)
                    messages.AddRange(ItemResourceChatMessageConverter.ToChatMessages(previousItems));
                    // Track the last message ID from the previous response's output
                    if (previousResponse.Output.Count > 0)
                    {
                        lastMessageId = previousResponse.Output[^1].Id;
                    }
                }
            }
        }

        // Add input items from the current request
        foreach (var inputMessage in request.Input.GetInputMessages())
        {
            messages.Add(inputMessage.ToChatMessage());
        }

        return (messages, lastMessageId);
    }

    private async Task<string?> LoadConversationAsync(List<ChatMessage> messages, string conversationId, CancellationToken cancellationToken)
    {
        // Get the conversation messages
        var conversationGrain = this._grainFactory.GetGrain<IConversationGrain>(conversationId);

        string? lastMessageId = null;
        // Use GetAllItemsAsync to stream all items
        await foreach (var itemResource in conversationGrain.GetAllItemsAsync(SortOrder.Ascending))
        {
            // Convert ItemResource to ChatMessage using converter
            messages.AddRange(ItemResourceChatMessageConverter.ToChatMessages([itemResource]));
            lastMessageId = itemResource.Id; // Track the last message ID
        }

        return lastMessageId;
    }
}
