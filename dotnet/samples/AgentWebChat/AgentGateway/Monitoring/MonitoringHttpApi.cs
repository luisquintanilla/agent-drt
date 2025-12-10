// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts;
using AgentContracts.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AgentGateway.Monitoring;

/// <summary>
/// HTTP API endpoints for system monitoring.
/// Provides real-time visibility into workflows, workers, and system health.
/// </summary>
internal static class MonitoringHttpApi
{
    /// <summary>
    /// Maps monitoring API endpoints to the application.
    /// </summary>
    public static void MapMonitoring(this WebApplication app)
    {
        var group = app.MapGroup("/v1/monitor")
            .WithTags("Monitoring");

        // System status overview
        group.MapGet("/status", GetSystemStatusAsync)
            .WithName("GetSystemStatus")
            .WithDescription("Get system-wide status overview including workflow counts and worker health")
            .Produces<SystemStatus>();

        // Worker endpoints
        group.MapGet("/workers", GetWorkersAsync)
            .WithName("GetWorkers")
            .WithDescription("Get status of all registered workers")
            .Produces<WorkerStatus[]>();

        group.MapGet("/workers/{workerId}", GetWorkerAsync)
            .WithName("GetWorker")
            .WithDescription("Get status of a specific worker")
            .Produces<WorkerStatus>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/workers/{workerId}/drain", DrainWorkerAsync)
            .WithName("DrainWorker")
            .WithDescription("Drain a worker (stop accepting new workflows)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/workers/{workerId}/enable", EnableWorkerAsync)
            .WithName("EnableWorker")
            .WithDescription("Re-enable a drained worker")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Workflow endpoints
        group.MapGet("/workflows/active", GetActiveWorkflowsAsync)
            .WithName("GetActiveWorkflows")
            .WithDescription("Get all currently active workflows (running, queued, or waiting)")
            .Produces<WorkflowMonitoringSummary[]>();

        group.MapGet("/workflows/recent", GetRecentWorkflowsAsync)
            .WithName("GetRecentWorkflows")
            .WithDescription("Get recent workflows (most recent first)")
            .Produces<WorkflowMonitoringSummary[]>();

        // Metrics endpoint
        group.MapGet("/metrics", GetWorkflowMetricsAsync)
            .WithName("GetWorkflowMetrics")
            .WithDescription("Get aggregated workflow metrics for a time window")
            .Produces<WorkflowMetricsSnapshot>();

        // SSE event stream
        group.MapGet("/events", StreamMonitoringEventsAsync)
            .WithName("StreamMonitoringEvents")
            .WithDescription("Stream real-time monitoring events via Server-Sent Events")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
    }

    private static async Task<IResult> GetSystemStatusAsync(
        IMonitoringService monitoringService,
        CancellationToken ct)
    {
        var status = await monitoringService.GetSystemStatusAsync(ct);
        return Results.Ok(status);
    }

    private static async Task<IResult> GetWorkersAsync(
        IMonitoringService monitoringService,
        CancellationToken ct)
    {
        var workers = await monitoringService.GetWorkersAsync(ct);
        return Results.Ok(workers);
    }

    private static async Task<IResult> GetWorkerAsync(
        string workerId,
        IMonitoringService monitoringService,
        CancellationToken ct)
    {
        var worker = await monitoringService.GetWorkerAsync(workerId, ct);
        if (worker is null)
        {
            return Results.Problem(
                title: "Worker not found",
                detail: $"Worker '{workerId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(worker);
    }

    private static async Task<IResult> DrainWorkerAsync(
        string workerId,
        IMonitoringService monitoringService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var success = await monitoringService.DrainWorkerAsync(workerId, ct);
        if (!success)
        {
            return Results.Problem(
                title: "Failed to drain worker",
                detail: $"Worker '{workerId}' not found or could not be drained.",
                statusCode: StatusCodes.Status404NotFound);
        }
        logger.LogInformation("Worker {WorkerId} drained", workerId);
        return Results.Ok(new { message = $"Worker '{workerId}' is now draining" });
    }

    private static async Task<IResult> EnableWorkerAsync(
        string workerId,
        IMonitoringService monitoringService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var success = await monitoringService.EnableWorkerAsync(workerId, ct);
        if (!success)
        {
            return Results.Problem(
                title: "Failed to enable worker",
                detail: $"Worker '{workerId}' not found or could not be enabled.",
                statusCode: StatusCodes.Status404NotFound);
        }
        logger.LogInformation("Worker {WorkerId} enabled", workerId);
        return Results.Ok(new { message = $"Worker '{workerId}' is now enabled" });
    }

    private static async Task<IResult> GetActiveWorkflowsAsync(
        IMonitoringService monitoringService,
        CancellationToken ct)
    {
        var workflows = await monitoringService.GetActiveWorkflowsAsync(ct);
        return Results.Ok(workflows);
    }

    private static async Task<IResult> GetRecentWorkflowsAsync(
        [FromQuery] int? count,
        IMonitoringService monitoringService,
        CancellationToken ct)
    {
        var workflows = await monitoringService.GetRecentWorkflowsAsync(count ?? 50, ct);
        return Results.Ok(workflows);
    }

    private static async Task<IResult> GetWorkflowMetricsAsync(
        [FromQuery] int? windowMinutes,
        IMonitoringService monitoringService,
        CancellationToken ct)
    {
        var window = TimeSpan.FromMinutes(windowMinutes ?? 60);
        var metrics = await monitoringService.GetWorkflowMetricsAsync(window, ct);
        return Results.Ok(metrics);
    }

    private static async Task StreamMonitoringEventsAsync(
        IMonitoringService monitoringService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Set up SSE response
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var options = AgentContractsJsonUtilities.DefaultOptions;

        logger.LogInformation("Client connected to monitoring event stream");

        try
        {
            // Send initial heartbeat
            await httpContext.Response.WriteAsync("event: connected\ndata: {\"message\":\"Connected to monitoring stream\"}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            await foreach (var evt in monitoringService.StreamEventsAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt, options);
                await httpContext.Response.WriteAsync($"event: {evt.EventType}\n", ct);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
            logger.LogInformation("Client disconnected from monitoring event stream");
        }
    }
}
