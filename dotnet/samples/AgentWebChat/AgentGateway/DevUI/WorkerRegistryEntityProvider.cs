// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.DevUI.Entities;

namespace AgentGateway.DevUI;

/// <summary>
/// Entity provider that discovers entities from remote workers via WorkerRegistry and WorkerDiscoveryCache.
/// </summary>
internal sealed class WorkerRegistryEntityProvider : IEntityProvider
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
        // Discover all entities (agents and workflows) from all workers
        var allEntities = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var worker in this._registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
        {
            IReadOnlyDictionary<string, EntityInfo>? entities = await this._cache.DiscoverEntitiesAsync(worker, cancellationToken).ConfigureAwait(false);
            if (entities is not null)
            {
                foreach (var (entityName, entityInfo) in entities)
                {
                    if (!allEntities.ContainsKey(entityName))
                    {
                        allEntities[entityName] = entityInfo;
                    }
                }
            }
        }

        foreach (var entityInfo in allEntities.Values)
        {
            yield return entityInfo;
        }
    }

    /// <inheritdoc/>
    public async Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        // Try to find the entity among discovered entities from all workers
        foreach (var worker in this._registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
        {
            var entities = await this._cache.DiscoverEntitiesAsync(worker, cancellationToken).ConfigureAwait(false);
            if (entities is not null && entities.TryGetValue(entityId, out var entityInfo))
            {
                return entityInfo;
            }
        }

        return null;
    }
}
