// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AgentContracts;
using Microsoft.Extensions.Options;

namespace AgentGateway;

/// <summary>
/// Tracks Worker instances registered with the gateway.
/// </summary>
internal sealed class WorkerRegistry
{
    /// <summary>
    /// Well-known ID for the default worker.
    /// </summary>
    public const string DefaultWorkerId = "__default__";

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly WorkerDiscoveryCache? _discoveryCache;

    /// <summary>
    /// Gets the default worker if one is configured, otherwise null.
    /// </summary>
    public WorkerInfo? DefaultWorker { get; }

    public WorkerRegistry(WorkerDiscoveryCache? discoveryCache, IOptions<AgentGatewayOptions> options)
    {
        this._discoveryCache = discoveryCache;

        // Register configured workers
        var workers = options.Value.Workers;
        for (int i = 0; i < workers.Count; i++)
        {
            var worker = workers[i];
            if (string.IsNullOrEmpty(worker.Endpoint))
            {
                continue;
            }

            var isDefault = i == 0;
            var hostId = worker.HostId ?? $"worker-{i + 1}";
            var id = isDefault ? DefaultWorkerId : worker.Endpoint;

            var workerInfo = new WorkerInfo(
                id,
                hostId,
                new Uri(worker.Endpoint, UriKind.Absolute),
                worker.HealthPath,
                worker.DiscoveryPath,
                IsDefault: isDefault);

            this._entries[id] = new Entry(workerInfo, DateTimeOffset.UtcNow);

            if (isDefault)
            {
                this.DefaultWorker = workerInfo;
            }
        }
    }

    public IReadOnlyCollection<WorkerInfo> ActiveWorkers => this._entries.Values
        .Where(e => !e.IsDown)
        .Select(e => e.Info)
        .ToList();

    /// <summary>
    /// Upserts a worker registration. If registrationId is supplied it is used as the key (allowing stable ids, eg instanceId) otherwise a new id is generated.
    /// Accepts a normalized representation: base endpoint plus relative health/discovery paths.
    /// </summary>
    /// <remarks>
    /// The request parameter should be validated using Data Annotations validation before calling this method.
    /// </remarks>
    public WorkerInfo Upsert(WorkerRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = request.Endpoint;

        var result = this._entries.AddOrUpdate(
            id,
            static (id, state) => new Entry(new WorkerInfo(
                id,
                state.HostId,
                new Uri(state.Endpoint, UriKind.Absolute),
                state.HealthPath,
                state.DiscoveryPath,
                IsDefault: false), DateTimeOffset.UtcNow),
            static (id, existing, state) => existing with
            {
                Info = existing.Info with
                {
                    HostId = state.HostId,
                    Endpoint = new Uri(state.Endpoint, UriKind.Absolute),
                    HealthPath = state.HealthPath,
                    DiscoveryPath = state.DiscoveryPath,
                },
                LastHeartbeat = DateTimeOffset.UtcNow,
                ConsecutiveFailures = 0,
                IsDown = false
            },
            request);

        return result.Info;
    }

    public bool Remove(string registrationId)
    {
        // Don't allow removal of the default worker
        if (registrationId == DefaultWorkerId)
        {
            return false;
        }

        var removed = this._entries.TryRemove(registrationId, out _);
        if (removed)
        {
            this._discoveryCache?.Invalidate(registrationId);
        }
        return removed;
    }

    public IEnumerable<Entry> Entries => this._entries.Values;

    public void MarkFailure(Entry entry, int failureThreshold)
    {
        string registrationId = entry.Info.Id;
        var newFailureCount = entry.ConsecutiveFailures + 1;

        // Don't remove the default worker on failure
        if (registrationId == DefaultWorkerId)
        {
            this._entries[registrationId] = entry with { ConsecutiveFailures = newFailureCount, IsDown = false };
            return;
        }

        if (newFailureCount >= failureThreshold)
        {
            this._entries.TryRemove(registrationId, out _);
            this._discoveryCache?.Invalidate(registrationId);
        }
        else
        {
            this._entries[registrationId] = entry with { ConsecutiveFailures = newFailureCount, IsDown = false };
            // Invalidate cache on any failure to ensure fresh discovery on next request
            this._discoveryCache?.Invalidate(registrationId);
        }
    }

    public void MarkSuccess(Entry entry)
        => this._entries[entry.Info.Id] = entry with { ConsecutiveFailures = 0, IsDown = false };

    /// <summary>
    /// Info about a worker. Endpoints are represented as a base endpoint plus relative paths.
    /// </summary>
    internal sealed record WorkerInfo(
        string Id,
        string HostId,
        Uri Endpoint,
        string HealthPath,
        string DiscoveryPath,
        bool IsDefault = false)
    {
        public Uri HealthUri { get; } = new Uri(Endpoint, HealthPath);
        public Uri DiscoveryUri { get; } = new Uri(Endpoint, DiscoveryPath);
    }

    internal sealed record Entry(WorkerInfo Info, DateTimeOffset LastHeartbeat)
    {
        public int ConsecutiveFailures { get; init; }
        public bool IsDown { get; init; }
    }
}
