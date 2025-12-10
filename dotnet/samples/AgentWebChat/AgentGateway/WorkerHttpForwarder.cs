// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace AgentGateway;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection")]
internal sealed class WorkerHttpForwarder
{
    private readonly WorkerRegistry _registry;
    private readonly IHttpForwarder _forwarder;
    private readonly ForwardingHttpClientProvider _clientProvider;
    private readonly WorkerDiscoveryCache _cache;

    public WorkerHttpForwarder(
        WorkerRegistry registry,
        IHttpForwarder forwarder,
        ForwardingHttpClientProvider clientProvider,
        WorkerDiscoveryCache cache)
    {
        this._registry = registry;
        this._forwarder = forwarder;
        this._clientProvider = clientProvider;
        this._cache = cache;
    }

    public async ValueTask ForwardRequestAsync(HttpContext context, string agent, string? id = null)
    {
        var worker = await this.SelectWorkerAsync(agent, context.RequestAborted);
        if (worker is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync($"No available worker supports agent '{agent}'");
            return;
        }

        var basePrefix = worker.Endpoint.ToString().TrimEnd('/');
        var httpClient = this._clientProvider.HttpClient;
        var error = await this._forwarder.SendAsync(context, basePrefix, httpClient, ForwarderRequestConfig.Empty, HttpTransformer.Default);
        if (error != ForwarderError.None && !context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    // Select the best available worker (if any) from the registry that supports the given agent.
    // Prefers non-default workers over the default worker.
    private async ValueTask<WorkerRegistry.WorkerInfo?> SelectWorkerAsync(string? agentName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentName))
        {
            // If no specific agent is requested, prefer any non-default worker, then any worker
            return this._registry.ActiveWorkers
                    .Where(w => w.Id != WorkerRegistry.DefaultWorkerId)
                    .FirstOrDefault(w => w.DiscoveryPath is not null)
                ?? this._registry.ActiveWorkers
                    .FirstOrDefault(w => w.Id != WorkerRegistry.DefaultWorkerId)
                ?? this._registry.ActiveWorkers
                    .FirstOrDefault(w => w.DiscoveryPath is not null)
                ?? this._registry.ActiveWorkers.FirstOrDefault();
        }

        // Query each non-default worker's discovery endpoint first to find one that supports the specific agent
        foreach (var worker in this._registry.ActiveWorkers.Where(w => w.Id != WorkerRegistry.DefaultWorkerId && w.DiscoveryPath is not null))
        {
            var entities = await this._cache.DiscoverEntitiesAsync(worker, cancellationToken);
            if (entities?.ContainsKey(agentName) == true)
            {
                return worker;
            }
        }

        // If no non-default worker supports this agent, check the default worker if it exists
        var defaultWorker = this._registry.ActiveWorkers.FirstOrDefault(w => w.IsDefault);
        if (defaultWorker is not null)
        {
            return defaultWorker;
        }

        // No worker found that supports this agent
        return null;
    }
}
