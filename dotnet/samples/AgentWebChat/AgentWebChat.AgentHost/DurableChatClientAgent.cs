// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost;

/// <summary>
/// Provides a durable agent implementation that delegates to an <see cref="IChatClient"/> with persistent message storage.
/// </summary>
/// <remarks>
/// This agent combines the functionality of <see cref="ChatClientAgent"/> with durable conversation management.
/// Messages are persisted using a custom thread storage mechanism, allowing conversations to be resumed across sessions.
/// The agent automatically rehydrates conversation history from the message store when provided with a conversation ID.
/// </remarks>
public sealed class DurableChatClientAgent : DurableAgent
{
    private readonly IChatClient _chatClient;
    private readonly string? _instructions;
    private readonly string? _name;
    private readonly Action<ChatOptions>? _configureChatOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableChatClientAgent"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client to use when running the agent.</param>
    /// <param name="instructions">
    /// Optional system instructions that guide the agent's behavior. These instructions are provided as a system message
    /// at the beginning of each conversation to establish the agent's role and behavior.
    /// </param>
    /// <param name="name">
    /// Optional name for the agent. This name is used for identification and logging purposes.
    /// </param>
    /// <param name="configureChatOptions">
    /// Optional action to configure chat options for the agent, such as adding tools, setting temperature, or other model parameters.
    /// This action is applied to the runtime options without replacing them.
    /// </param>
    public DurableChatClientAgent(
        IChatClient chatClient,
        string? instructions = null,
        string? name = null,
        Action<ChatOptions>? configureChatOptions = null)
    {
        this._chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        this._instructions = instructions;
        this._name = name;
        this._configureChatOptions = configureChatOptions;
    }

    /// <inheritdoc/>
    public override string? Name => this._name;

    /// <inheritdoc/>
    public override async Task<AgentRunResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
        => await this.RunStreamingAsync(messages, thread, options, cancellationToken)
            .ToAgentRunResponseAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        ChatOptions? chatClientOptions = (options as ChatClientAgentRunOptions)?.ChatOptions?.Clone();

        // Apply agent-level configuration to the chat options
        if (this._configureChatOptions is not null)
        {
            chatClientOptions ??= new ChatOptions();
            this._configureChatOptions(chatClientOptions);
        }

        var durableThread = this.GetNewThread(options?.Features)!;

        // Notify the thread of new incoming messages
        await durableThread.MessagesReceivedAsync(messages, cancellationToken).ConfigureAwait(false);

        // Build the message sequence: system prompt + existing messages + new messages
        List<ChatMessage> conversationMessages = [];

        // Add system instructions if provided
        if (!string.IsNullOrWhiteSpace(this._instructions))
        {
            conversationMessages.Add(new ChatMessage(ChatRole.System, this._instructions));
        }

        // Add existing messages from the thread's message store
        IEnumerable<ChatMessage> existingMessages = await durableThread.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
        conversationMessages.AddRange(existingMessages);

        // Add the new input messages
        conversationMessages.AddRange(messages);

        // Stream the response from the chat client
        List<AgentRunResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in this._chatClient.GetStreamingResponseAsync(conversationMessages, chatClientOptions, cancellationToken))
        {
            AgentRunResponseUpdate agentUpdate = new(update);
            updates.Add(agentUpdate);
            yield return agentUpdate;
        }

        // Convert updates to final response to get the complete messages
        AgentRunResponse response = updates.ToAgentRunResponse();

        // Store the response messages in the thread
        await durableThread.MessagesReceivedAsync(response.Messages, cancellationToken).ConfigureAwait(false);
    }
}
