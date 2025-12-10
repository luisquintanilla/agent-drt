// Copyright (c) Microsoft. All rights reserved.

using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts;
using AgentContracts.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AgentGateway;

internal static class WorkerManagementHttpApi
{
    public static void MapWorkerManagement(this WebApplication app)
    {
        app.MapPost("/workers/registrations", RegisterAsync).WithName("WorkerRegister");
        app.MapDelete("/workers/registrations", Deregister).WithName("WorkerDeregister");
    }

    private static async Task<IResult> RegisterAsync(
        WorkerRegistrationRequest request,
        WorkerRegistry registry,
        IHttpClientFactory httpClientFactory,
        IMonitoringEventBroadcaster eventBroadcaster,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var isNew = !registry.Entries.Any(e => e.Info.Endpoint.ToString() == request.Endpoint);
        var workerInfo = registry.Upsert(request);
        logger.LogInformation("Worker registration/heartbeat from host {HostId}. Endpoint={Endpoint} HealthPath={HealthPath} DiscoveryPath={DiscoveryPath}", request.HostId, request.Endpoint, request.HealthPath, request.DiscoveryPath);

        // Publish worker event
        eventBroadcaster.PublishWorkerEvent(
            isNew ? MonitoringEventTypes.WorkerRegistered : MonitoringEventTypes.WorkerHealthChanged,
            new WorkerEventPayload
            {
                WorkerId = workerInfo.Id,
                Health = "Healthy"
            });

        return Results.Ok(new WorkerRegistrationResponse { RegistrationId = workerInfo.Id });
    }

    private static IResult Deregister(
        [FromQuery] string endpoint,
        WorkerRegistry registry,
        IMonitoringEventBroadcaster eventBroadcaster,
        ILogger<Program> logger)
    {
        // Get worker info before removal
        var worker = registry.Entries.FirstOrDefault(e => e.Info.Endpoint.ToString() == endpoint);
        var workerId = worker?.Info.Id;

        if (registry.Remove(endpoint))
        {
            logger.LogInformation("Worker '{Endpoint}' deregistered", endpoint);

            // Publish worker deregistered event
            if (workerId is not null)
            {
                eventBroadcaster.PublishWorkerEvent(MonitoringEventTypes.WorkerDeregistered, new WorkerEventPayload
                {
                    WorkerId = workerId
                });
            }

            return Results.NoContent();
        }

        return Results.NotFound();
    }
}
