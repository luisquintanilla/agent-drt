// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentGateway.Health;

/// <summary>
/// Health check for the Agent Gateway that verifies worker connectivity and entity discovery.
/// </summary>
internal class AgentGatewayHealthCheck : IHealthCheck
{
    private readonly WorkerRegistry _registry;
    private readonly WorkerDiscoveryCache _cache;

    public AgentGatewayHealthCheck(WorkerRegistry registry, WorkerDiscoveryCache cache)
    {
        this._registry = registry;
        this._cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Count all available entities
            var entitiesCount = 0;
            var workerCount = 0;

            foreach (var worker in this._registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
            {
                workerCount++;
                var supportedAgents = await this._cache.DiscoverAgentsAsync(worker, cancellationToken);
                if (supportedAgents is not null)
                {
                    entitiesCount += supportedAgents.Count;
                }
            }

            var data = new Dictionary<string, object>
            {
                ["entities_count"] = entitiesCount,
                ["workers_count"] = workerCount,
                ["framework"] = "agent_framework"
            };

            return HealthCheckResult.Healthy(
                $"Agent Gateway is healthy with {entitiesCount} entities from {workerCount} workers",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Agent Gateway is unhealthy",
                ex,
                new Dictionary<string, object>
                {
                    ["entities_count"] = 0,
                    ["workers_count"] = 0,
                    ["framework"] = "agent_framework"
                });
        }
    }
}
