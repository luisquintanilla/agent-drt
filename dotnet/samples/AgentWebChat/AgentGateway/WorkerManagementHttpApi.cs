// Copyright (c) Microsoft. All rights reserved.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts;
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
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var workerInfo = registry.Upsert(request);
        logger.LogInformation("Worker registration/heartbeat from host {HostId}. Endpoint={Endpoint} HealthPath={HealthPath} DiscoveryPath={DiscoveryPath}", request.HostId, request.Endpoint, request.HealthPath, request.DiscoveryPath);
        return Results.Ok(new WorkerRegistrationResponse { RegistrationId = workerInfo.Id });
    }

    private static IResult Deregister(
        [FromQuery] string endpoint,
        WorkerRegistry registry,
        ILogger<Program> logger)
    {
        if (registry.Remove(endpoint))
        {
            logger.LogInformation("Worker '{Endpoint}' deregistered", endpoint);
            return Results.NoContent();
        }

        return Results.NotFound();
    }
}
