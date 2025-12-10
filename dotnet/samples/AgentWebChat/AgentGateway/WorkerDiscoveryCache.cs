// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.DevUI.Entities;
using Microsoft.Extensions.Logging;

namespace AgentGateway;

/// <summary>
/// Caches the results of worker discovery calls to avoid repeated HTTP requests.
/// Cache entries are automatically invalidated when workers are removed or when they expire.
/// </summary>
internal sealed partial class WorkerDiscoveryCache
{
    private readonly ConcurrentDictionary<string, CachedDiscoveryResult> _cache = new();
    private readonly TimeSpan _cacheDuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WorkerDiscoveryCache> _logger;

    public WorkerDiscoveryCache(
        HttpClient httpClient,
        ILogger<WorkerDiscoveryCache> logger,
        TimeSpan? cacheDuration = null)
    {
        this._httpClient = httpClient;
        this._logger = logger;
        this._cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Attempts to get a cached discovery result for a worker.
    /// Returns null if no valid cache entry exists or if the entry has expired.
    /// </summary>
    public CachedDiscoveryResult? TryGet(string workerId)
    {
        if (this._cache.TryGetValue(workerId, out var cached))
        {
            if (DateTimeOffset.UtcNow - cached.Timestamp < this._cacheDuration)
            {
                return cached;
            }

            // Entry expired, remove it
            this._cache.TryRemove(workerId, out _);
        }

        return null;
    }

    /// <summary>
    /// Stores a successful discovery result in the cache.
    /// </summary>
    public void Set(string workerId, IReadOnlyDictionary<string, EntityInfo> entities)
    {
        this._cache[workerId] = new CachedDiscoveryResult(
            workerId,
            entities,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Removes a cache entry for a specific worker.
    /// This should be called when a worker is removed from the registry or marked as failed.
    /// </summary>
    public void Invalidate(string workerId)
    {
        this._cache.TryRemove(workerId, out _);
    }

    /// <summary>
    /// Removes all cache entries.
    /// </summary>
    public void Clear()
    {
        this._cache.Clear();
    }

    /// <summary>
    /// Removes expired cache entries and entries for workers that are no longer in the registry.
    /// </summary>
    public void Cleanup(IEnumerable<string> activeWorkerIds)
    {
        var now = DateTimeOffset.UtcNow;
        var activeIds = new HashSet<string>(activeWorkerIds);

        foreach (var kvp in this._cache)
        {
            // Remove if expired or worker is no longer active
            if (now - kvp.Value.Timestamp >= this._cacheDuration || !activeIds.Contains(kvp.Key))
            {
                this._cache.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Discovers which entities (agents and workflows) a worker supports, using the cache if available.
    /// Returns a dictionary of supported entities keyed by entity name, or null if discovery fails.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<string, EntityInfo>?> DiscoverEntitiesAsync(WorkerRegistry.WorkerInfo worker, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cached = this.TryGet(worker.Id);
        if (cached is not null)
        {
            return cached.Entities;
        }

        // Cache miss - perform discovery
        try
        {
            var discoveryUri = worker.DiscoveryUri;
            var discoveryResponse = await this._httpClient.GetFromJsonAsync(
                discoveryUri,
                AgentGatewayJsonContext.Default.DiscoveryResponse,
                cancellationToken);

            if (discoveryResponse?.Entities is { Count: > 0 } entities)
            {
                // Cache the successful discovery result as a dictionary
                var entityDict = entities
                    .Where(entity => !string.IsNullOrEmpty(entity.Name))
                    .ToDictionary(entity => entity.Name, entity => entity, StringComparer.OrdinalIgnoreCase);
                this.Set(worker.Id, entityDict);
                return entityDict;
            }

            if (discoveryResponse is not null)
            {
                // Cache an empty dictionary to avoid repeated calls when worker reports no entities
                var emptyEntities = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase);
                this.Set(worker.Id, emptyEntities);
                return emptyEntities;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(
                ex,
                "Worker discovery threw exception for worker {WorkerId} at {DiscoveryUri}",
                worker.Id,
                worker.DiscoveryUri);
        }

        return null;
    }

    public sealed record CachedDiscoveryResult(
        string WorkerId,
        IReadOnlyDictionary<string, EntityInfo> Entities,
        DateTimeOffset Timestamp);
}
