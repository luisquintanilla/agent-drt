// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentGateway.Models;
using Orleans;
using Orleans.Runtime;

namespace AgentGateway.Conversations;

/// <summary>
/// State for an agent conversation index grain.
/// </summary>
[GenerateSerializer]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class AgentConversationIndexState
{
    /// <summary>
    /// Set of conversation IDs associated with this agent.
    /// </summary>
    [Id(0)]
    public HashSet<string> ConversationIds { get; set; } = [];
}

/// <summary>
/// Grain interface for managing conversation index for a single agent.
/// </summary>
public interface IAgentConversationIndexGrain : IGrainWithStringKey
{
    /// <summary>
    /// Adds a conversation ID to the agent's index.
    /// </summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task AddConversationAsync(string conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a conversation ID from the agent's index.
    /// </summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RemoveConversationAsync(string conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all conversation IDs for this agent.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of conversation IDs.</returns>
    Task<IReadOnlyList<string>> GetConversationIdsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Orleans grain implementation for managing conversation index for a single agent.
/// Each grain instance represents one agent and tracks all conversations associated with that agent.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class AgentConversationIndexGrain([PersistentState("state")] IPersistentState<AgentConversationIndexState> indexState) : Grain, IAgentConversationIndexGrain
{
    public async Task AddConversationAsync(string conversationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId);

        if (indexState.State.ConversationIds.Add(conversationId))
        {
            await indexState.WriteStateAsync(cancellationToken);
        }
    }

    public async Task RemoveConversationAsync(string conversationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId);

        if (indexState.State.ConversationIds.Remove(conversationId))
        {
            await indexState.WriteStateAsync(cancellationToken);
        }
    }

    public Task<IReadOnlyList<string>> GetConversationIdsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> conversationIds = indexState.State.ConversationIds.ToArray();
        return Task.FromResult(conversationIds);
    }
}

/// <summary>
/// Orleans-backed implementation of IAgentConversationIndex.
/// Uses one grain per agent ID to track conversations for each agent.
/// This is a non-standard extension to the OpenAI Conversations API.
/// </summary>
/// <remarks>
/// <para>
/// This implementation provides persistent, distributed indexing of conversations by agent ID using Orleans grains.
/// Each agent has its own grain instance that maintains a set of conversation IDs associated with that agent.
/// </para>
/// <para>
/// To use this implementation in your application, register it in your dependency injection container:
/// <code>
/// builder.Services.AddSingleton&lt;IAgentConversationIndex, OrleansAgentConversationIndex&gt;();
/// </code>
/// </para>
/// <para>
/// This replaces the in-memory implementation (<see cref="InMemoryAgentConversationIndex"/>) with a
/// persistent, scalable solution suitable for production scenarios.
/// </para>
/// </remarks>
internal sealed class OrleansAgentConversationIndex(IGrainFactory grainFactory) : IAgentConversationIndex
{
    /// <inheritdoc />
    public async Task AddConversationAsync(string agentId, string conversationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        ArgumentException.ThrowIfNullOrEmpty(conversationId);

        var grain = grainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        await grain.AddConversationAsync(conversationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveConversationAsync(string agentId, string conversationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        ArgumentException.ThrowIfNullOrEmpty(conversationId);

        var grain = grainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        await grain.RemoveConversationAsync(conversationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ListResponse<string>> GetConversationIdsAsync(string agentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);

        var grain = grainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        var results = await grain.GetConversationIdsAsync(cancellationToken);
        return new ListResponse<string> { Data = [.. results], HasMore = false };
    }
}
