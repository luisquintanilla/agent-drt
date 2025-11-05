// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost;

public class ConfigRolloutAgent : DurableAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<ConfigRolloutAgent> _logger;

    public override string? Name => "config-rollout";

    public ConfigRolloutAgent(
        IChatClient chatClient,
        ILogger<ConfigRolloutAgent> logger)
    {
        this._chatClient = chatClient;
        this._logger = logger;
    }

    public override async Task<AgentRunResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => await this.RunStreamingAsync(messages, thread, options, cancellationToken).ToAgentRunResponseAsync(cancellationToken);

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        bool done = false;

        void MarkComplete() => done = true;

        async Task UpdateComponent(string componentResourceId, string newVersion)
        {
            this._logger.LogInformation("Updating component {ComponentResourceId} to version {NewVersion}", componentResourceId, newVersion);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            this._logger.LogInformation("Component {ComponentResourceId} updated to version {NewVersion}", componentResourceId, newVersion);
        }

        var chatClientOptions = (options as ChatClientAgentRunOptions)?.ChatOptions?.Clone() ?? new();
        chatClientOptions.Tools =
        [
            AIFunctionFactory.Create(MarkComplete, "update_complete", "Signals to the system that all updates are complete."),
            AIFunctionFactory.Create(UpdateComponent, "update_component", "Updates a component to a new version.")
        ];

        var durableThread = this.GetNewThread(options?.Features);

        await durableThread.MessagesReceivedAsync(messages, cancellationToken);

        var systemPrompt = new ChatMessage(ChatRole.System, """
            You are a configuration rollout agent. You can update components.
            Send the user text updates as you complete each action.
            If/when you have updated the components, mark the task as complete by invoking the 'update_complete' tool.
            """);

        var existingMessages = await durableThread.GetMessagesAsync(cancellationToken);
        List<ChatMessage> internalThread = [systemPrompt, .. existingMessages, .. messages];
        while (!done)
        {
            var updates = new List<AgentRunResponseUpdate>();
            await foreach (var update in this._chatClient.GetStreamingResponseAsync(internalThread, chatClientOptions, cancellationToken))
            {
                var agentUpdate = new AgentRunResponseUpdate(update);
                updates.Add(agentUpdate);
                yield return agentUpdate;
            }

            var response = updates.ToAgentRunResponse();
            internalThread.AddRange(response.Messages);
            internalThread.Add(new ChatMessage(ChatRole.User,
                """
                If/when you have updated the components, mark the task as complete by invoking the 'update_complete' tool.
                """));
            await durableThread.MessagesReceivedAsync(response.Messages, cancellationToken);
        }
    }
}
