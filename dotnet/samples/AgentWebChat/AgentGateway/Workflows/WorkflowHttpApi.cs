// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts;
using AgentContracts.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace AgentGateway.Workflows;

/// <summary>
/// HTTP API endpoints for workflow management.
/// Implements both the frontend API (IWorkflowClient) and the state callback API (IWorkflowStateService).
/// </summary>
internal static class WorkflowHttpApi
{
    private const string IfMatchHeader = "If-Match";

    /// <summary>
    /// Maps workflow API endpoints to the application.
    /// </summary>
    public static void MapWorkflows(this WebApplication app)
    {
        var group = app.MapGroup("/v1/workflows");

        // ============ Frontend API (IWorkflowClient) ============

        // GET /v1/workflows - List workflows
        group.MapGet("/", ListWorkflowsAsync)
            .WithName("ListWorkflows")
            .Produces<WorkflowListResponse<WorkflowRunSummary>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // POST /v1/workflows - Start a new workflow
        group.MapPost("/", StartWorkflowAsync)
            .WithName("StartWorkflow")
            .Produces<WorkflowRun>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /v1/workflows/{runId} - Get workflow status
        group.MapGet("/{runId}", GetWorkflowAsync)
            .WithName("GetWorkflow")
            .Produces<WorkflowRun>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /v1/workflows/{runId}/events - Stream workflow events (SSE)
        group.MapGet("/{runId}/events", StreamWorkflowEventsAsync)
            .WithName("StreamWorkflowEvents")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /v1/workflows/{runId}/signals - Send a signal to a workflow
        group.MapPost("/{runId}/signals", SendSignalAsync)
            .WithName("SendWorkflowSignal")
            .Produces<WorkflowRun>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /v1/workflows/{runId}/cancel - Request cooperative cancellation
        group.MapPost("/{runId}/cancel", CancelWorkflowAsync)
            .WithName("CancelWorkflow")
            .Produces<WorkflowRun>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /v1/workflows/{runId}/abort - Forcefully abort workflow
        group.MapPost("/{runId}/abort", AbortWorkflowAsync)
            .WithName("AbortWorkflow")
            .Produces<WorkflowRun>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // DELETE /v1/workflows/{runId} - Permanently delete a workflow
        group.MapDelete("/{runId}", DeleteWorkflowAsync)
            .WithName("DeleteWorkflow")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // ============ State Callback API (IWorkflowStateService) - called by AgentHost ============

        var stateGroup = app.MapGroup("/v1/workflows/{runId}/state");

        // PUT /v1/workflows/{runId}/state/status - Update workflow status
        stateGroup.MapPut("/status", UpdateStatusAsync)
            .WithName("UpdateWorkflowStatus")
            .Produces<ETagResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /v1/workflows/{runId}/state/steps/started - Record step started
        stateGroup.MapPost("/steps/started", RecordStepStartedAsync)
            .WithName("RecordStepStarted")
            .Produces<ETagResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /v1/workflows/{runId}/state/steps/completed - Record step completed
        stateGroup.MapPost("/steps/completed", RecordStepCompletedAsync)
            .WithName("RecordStepCompleted")
            .Produces<ETagResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /v1/workflows/{runId}/state/pending-requests - Record pending request
        stateGroup.MapPost("/pending-requests", RecordPendingRequestAsync)
            .WithName("RecordPendingRequest")
            .Produces<ETagResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // DELETE /v1/workflows/{runId}/state/pending-requests/{requestId} - Clear pending request
        stateGroup.MapDelete("/pending-requests/{requestId}", ClearPendingRequestAsync)
            .WithName("ClearPendingRequest")
            .Produces<ETagResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // PUT /v1/workflows/{runId}/state/checkpoint - Save checkpoint
        stateGroup.MapPut("/checkpoint", SaveCheckpointAsync)
            .WithName("SaveCheckpoint")
            .Produces<ETagResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // GET /v1/workflows/{runId}/state/checkpoint - Get checkpoint
        stateGroup.MapGet("/checkpoint", GetCheckpointAsync)
            .WithName("GetCheckpoint")
            .Produces<WorkflowCheckpointResult>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /v1/workflows/{runId}/state/artifacts - Record artifact
        stateGroup.MapPost("/artifacts", RecordArtifactAsync)
            .WithName("RecordArtifact")
            .Produces<ETagResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    // ============ Frontend API Handlers ============

    private static async Task<IResult> ListWorkflowsAsync(
        [FromQuery] string? status,
        [FromQuery] int? limit,
        [FromQuery] string? after,
        [FromQuery] string? before,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        WorkflowRunStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<WorkflowRunStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        var response = await indexGrain.ListAsync(
            statusFilter,
            limit ?? 50,
            after,
            before,
            ct);

        return Results.Ok(response);
    }

    private static async Task<IResult> StartWorkflowAsync(
        StartWorkflowRequest request,
        IGrainFactory grainFactory,
        IWorkflowExecutor workflowExecutor,
        IOptions<AgentGatewayOptions> gatewayOptions,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var runId = $"wfrun_{Guid.NewGuid():N}";
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            // Create the workflow run in the grain
            var run = await grain.StartAsync(request, ct);
            logger.LogInformation("Workflow started: {RunId} (workflow: {WorkflowName})", runId, request.WorkflowName);

            // Build callback URL for AgentHost to call back to Gateway
            var callbackBaseUrl = gatewayOptions.Value.CallbackBaseUrl;
            if (string.IsNullOrEmpty(callbackBaseUrl))
            {
                // Infer from request context
                var scheme = httpContext.Request.Scheme;
                var host = httpContext.Request.Host.ToString();
                callbackBaseUrl = $"{scheme}://{host}";
            }

            // Dispatch execution to a worker
            var executionRequest = new WorkflowExecutionRequest
            {
                RunId = runId,
                WorkflowName = request.WorkflowName,
                Input = request.Input,
                CallbackBaseUrl = callbackBaseUrl,
                Options = request.Options
            };

            // Dispatch and await the result - the executor streams SSE events but we just need acknowledgment
            var result = await workflowExecutor.ExecuteAsync(executionRequest, preferredWorkerId: null, ct);
            if (!result.Success)
            {
                logger.LogError(
                    "Workflow execution dispatch failed: {RunId}, Error: {ErrorCode} - {ErrorMessage}",
                    runId, result.ErrorCode, result.ErrorMessage);

                // Update workflow status to failed
                await grain.AbortAsync($"Dispatch failed: {result.ErrorMessage}", ct);

                return Results.Problem(
                    title: "Workflow dispatch failed",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            // Persist the assigned worker for future resume requests
            if (!string.IsNullOrEmpty(result.WorkerId))
            {
                await grain.SetAssignedWorkerIdAsync(result.WorkerId, ct);
            }

            return Results.Created($"/v1/workflows/{runId}", run);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start workflow {WorkflowName}", request.WorkflowName);
            return Results.Problem(
                title: "Failed to start workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> GetWorkflowAsync(
        string runId,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);
        var run = await grain.GetAsync(ct);

        if (run is null)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(run);
    }

    private static async Task StreamWorkflowEventsAsync(
        string runId,
        [FromQuery] int? after,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        // Check if workflow exists
        var run = await grain.GetAsync(ct);
        if (run is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Workflow not found", runId }, ct);
            return;
        }

        // Set up SSE response
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var options = AgentContractsJsonUtilities.DefaultOptions;

        await foreach (var evt in grain.StreamEventsAsync(after, ct))
        {
            var eventType = evt.GetType().Name;
            var json = JsonSerializer.Serialize(evt, options);

            await httpContext.Response.WriteAsync($"event: {eventType}\n", ct);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }

        // Send done event
        await httpContext.Response.WriteAsync("event: done\ndata: {}\n\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }

    private static async Task<IResult> SendSignalAsync(
        string runId,
        WorkflowSignal signal,
        IGrainFactory grainFactory,
        IWorkflowExecutor workflowExecutor,
        IOptions<AgentGatewayOptions> gatewayOptions,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            // Get current workflow state for resume context
            var currentRun = await grain.GetAsync(ct);
            if (currentRun is null)
            {
                return Results.Problem(
                    title: "Workflow not found",
                    detail: $"Workflow '{runId}' not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            // Record the signal in the grain
            var run = await grain.SendSignalAsync(signal, ct);
            logger.LogInformation("Signal sent to workflow {RunId}: request {RequestId}", runId, signal.RequestId);

            // Build callback URL
            var callbackBaseUrl = gatewayOptions.Value.CallbackBaseUrl;
            if (string.IsNullOrEmpty(callbackBaseUrl))
            {
                var scheme = httpContext.Request.Scheme;
                var host = httpContext.Request.Host.ToString();
                callbackBaseUrl = $"{scheme}://{host}";
            }

            // Get checkpoint data and assigned worker for resume
            var checkpointResult = await grain.GetCheckpointAsync(ct);
            var assignedWorkerId = await grain.GetAssignedWorkerIdAsync(ct);

            // Dispatch resume to a worker
            var resumeRequest = new WorkflowResumeRequest
            {
                RunId = runId,
                WorkflowName = currentRun.WorkflowName,
                CallbackBaseUrl = callbackBaseUrl,
                Signal = signal,
                CheckpointData = checkpointResult?.Checkpoint.Data
            };

            // Dispatch and await the result
            var result = await workflowExecutor.ResumeAsync(resumeRequest, assignedWorkerId, ct);
            if (!result.Success)
            {
                logger.LogError(
                    "Workflow resume dispatch failed: {RunId}, Error: {ErrorCode} - {ErrorMessage}",
                    runId, result.ErrorCode, result.ErrorMessage);

                // Update workflow status to failed
                await grain.AbortAsync($"Resume dispatch failed: {result.ErrorMessage}", ct);

                return Results.Problem(
                    title: "Workflow resume failed",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Ok(run);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Invalid signal",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> CancelWorkflowAsync(
        string runId,
        IGrainFactory grainFactory,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var run = await grain.CancelAsync(ct);
            logger.LogInformation("Workflow cancellation requested: {RunId}", runId);
            return Results.Ok(run);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Cannot cancel workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> AbortWorkflowAsync(
        string runId,
        AbortWorkflowRequest request,
        IGrainFactory grainFactory,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var run = await grain.AbortAsync(request.Reason, ct);
            logger.LogWarning("Workflow aborted: {RunId} - Reason: {Reason}", runId, request.Reason);
            return Results.Ok(run);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Cannot abort workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> DeleteWorkflowAsync(
        string runId,
        IGrainFactory grainFactory,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            // Check if workflow exists first
            var run = await grain.GetAsync(ct);
            if (run is null)
            {
                return Results.Problem(
                    title: "Workflow not found",
                    detail: $"Workflow '{runId}' not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            await grain.DeleteAsync(ct);
            logger.LogInformation("Workflow deleted: {RunId}", runId);
            return Results.NoContent();
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Cannot delete workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    // ============ State Callback API Handlers ============

    private static async Task<IResult> UpdateStatusAsync(
        string runId,
        WorkflowRunStatusUpdate update,
        [FromHeader(Name = IfMatchHeader)] string? etag,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.UpdateStatusAsync(update, etag, ct);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(
                title: "Concurrency conflict",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RecordStepStartedAsync(
        string runId,
        WorkflowStepStartedRecord step,
        [FromHeader(Name = IfMatchHeader)] string? etag,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.RecordStepStartedAsync(step, etag, ct);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(
                title: "Concurrency conflict",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RecordStepCompletedAsync(
        string runId,
        WorkflowStepCompletedRecord step,
        [FromHeader(Name = IfMatchHeader)] string? etag,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.RecordStepCompletedAsync(step, etag, ct);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(
                title: "Concurrency conflict",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RecordPendingRequestAsync(
        string runId,
        PendingExternalRequest request,
        [FromHeader(Name = IfMatchHeader)] string? etag,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.RecordPendingRequestAsync(request, etag, ct);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(
                title: "Concurrency conflict",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> ClearPendingRequestAsync(
        string runId,
        string requestId,
        [FromHeader(Name = IfMatchHeader)] string? etag,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.ClearPendingRequestAsync(requestId, etag, ct);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(
                title: "Concurrency conflict",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> SaveCheckpointAsync(
        string runId,
        WorkflowCheckpointData checkpoint,
        [FromHeader(Name = IfMatchHeader)] string? etag,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.SaveCheckpointAsync(checkpoint, etag, ct);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(
                title: "Concurrency conflict",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> GetCheckpointAsync(
        string runId,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var checkpoint = await grain.GetCheckpointAsync(ct);
            if (checkpoint is null)
            {
                return Results.NoContent();
            }
            return Results.Ok(checkpoint);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> RecordArtifactAsync(
        string runId,
        WorkflowArtifactRecord artifact,
        [FromHeader(Name = IfMatchHeader)] string? etag,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.RecordArtifactAsync(artifact, etag, ct);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(
                title: "Concurrency conflict",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }
}

/// <summary>
/// Response containing the new ETag after a state update.
/// </summary>
internal sealed class ETagResponse
{
    public required string ETag { get; init; }
}
