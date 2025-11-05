// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentGateway;

/// <summary>
/// Periodically probes registered workers for health status.
/// </summary>
internal sealed class WorkerHealthCheckService : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);
    private readonly int _failureThreshold = 3;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorkerRegistry _registry;
    private readonly WorkerDiscoveryCache _discoveryCache;
    private readonly ILogger<WorkerHealthCheckService> _logger;

    public WorkerHealthCheckService(
        IHttpClientFactory httpClientFactory,
        WorkerRegistry registry,
        WorkerDiscoveryCache discoveryCache,
        ILogger<WorkerHealthCheckService> logger)
    {
        this._httpClientFactory = httpClientFactory;
        this._registry = registry;
        this._discoveryCache = discoveryCache;
        this._logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = this._httpClientFactory.CreateClient();
        async Task ProbeOnceAsync()
        {
            foreach (var entry in this._registry.Entries)
            {
                var healthUri = entry.Info.HealthUri;
                if (healthUri is null)
                {
                    continue;
                }

                try
                {
                    using var response = await client.GetAsync(healthUri, stoppingToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        this._registry.MarkSuccess(entry);
                    }
                    else
                    {
                        this._registry.MarkFailure(entry, this._failureThreshold);
                        this._logger.LogWarning("Health probe failed for worker {RegistrationId} host {HostId} with status {Status}. FailureCount={Count}", entry.Info.Id, entry.Info.HostId, response.StatusCode, entry.ConsecutiveFailures + 1);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    this._registry.MarkFailure(entry, this._failureThreshold);
                    this._logger.LogWarning(ex, "Health probe threw for worker {RegistrationId} host {HostId}. FailureCount={Count}", entry.Info.Id, entry.Info.HostId, entry.ConsecutiveFailures + 1);
                }
            }
        }

        try
        {
            await ProbeOnceAsync().ConfigureAwait(false);

            // Cleanup expired cache entries
            this._discoveryCache.Cleanup(this._registry.Entries.Select(e => e.Info.Id));

            using var timer = new PeriodicTimer(this._interval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProbeOnceAsync().ConfigureAwait(false);
                // Cleanup expired cache entries and entries for removed workers
                this._discoveryCache.Cleanup(this._registry.Entries.Select(e => e.Info.Id));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            this._logger.LogError(ex, "Unexpected error in worker health check loop.");
        }
    }
}
