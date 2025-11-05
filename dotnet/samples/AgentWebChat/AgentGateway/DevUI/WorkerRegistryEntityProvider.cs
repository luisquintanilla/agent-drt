// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts;
using AgentGateway.DevUI.Entities;

namespace AgentGateway.DevUI;

/// <summary>
/// Entity provider that discovers entities from remote workers via WorkerRegistry and WorkerDiscoveryCache.
/// </summary>
internal sealed class WorkerRegistryEntityProvider
{
    private readonly WorkerRegistry _registry;
    private readonly WorkerDiscoveryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerRegistryEntityProvider"/> class.
    /// </summary>
    /// <param name="registry">The worker registry containing registered workers.</param>
    /// <param name="cache">The worker discovery cache for discovering agents from workers.</param>
    public WorkerRegistryEntityProvider(WorkerRegistry registry, WorkerDiscoveryCache cache)
    {
        this._registry = registry;
        this._cache = cache;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<EntityInfo> GetEntitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Discover agents from all workers
        var allAgents = new Dictionary<string, AgentDiscoveryCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var worker in this._registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
        {
            IReadOnlyDictionary<string, AgentDiscoveryCard>? supportedAgents = await this._cache.DiscoverAgentsAsync(worker, cancellationToken).ConfigureAwait(false);
            if (supportedAgents is not null)
            {
                foreach (var (agentName, agentCard) in supportedAgents)
                {
                    if (!allAgents.ContainsKey(agentName))
                    {
                        allAgents[agentName] = agentCard;
                    }
                }
            }
        }

        // Convert agents to EntityInfo
        foreach (var (agentId, agentCard) in allAgents)
        {
            var tools = new List<JsonElement>();

            yield return new EntityInfo(
                Id: agentId,
                Type: "agent",
                Name: agentCard.Name ?? agentId,
                Description: agentCard.Description,
                Framework: "agent-framework",
                Tools: tools,
                Metadata: []
            )
            {
                Source = "directory"
            };
        }

        // TODO: Add workflow discovery when workflow support is implemented
    }

    /// <inheritdoc/>
    public async Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        // Try to find the entity among discovered agents
        foreach (var worker in this._registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
        {
            var supportedAgents = await this._cache.DiscoverAgentsAsync(worker, cancellationToken).ConfigureAwait(false);
            if (supportedAgents is not null && supportedAgents.TryGetValue(entityId, out var agentCard))
            {
                var tools = new List<JsonElement>();
                // AgentDiscoveryCard.Tools property access removed - not available in current schema

                return new EntityInfo(
                    Id: entityId,
                    Type: "agent",
                    Name: agentCard.Name ?? entityId,
                    Description: agentCard.Description,
                    Framework: "agent-framework",
                    Tools: tools,
                    Metadata: []
                )
                {
                    Source = "directory"
                };
            }
        }

        // TODO: Check workflows when workflow support is implemented

        return null;
    }
}
