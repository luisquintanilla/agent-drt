// Copyright (c) Microsoft. All rights reserved.

using System.Net.Mime;
using AgentContracts;
using AgentContracts.Workflows;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// HTTP API endpoints for the workflow host.
/// These endpoints are called by the Gateway to execute/resume workflows.
/// </summary>
public static class WorkflowHttpApi
{
    /// <summary>
    /// Maps the workflow host HTTP endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkflowHost(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/workflow-host")
            .WithTags("Workflow Host");

        group.MapPost("/execute", ExecuteWorkflowAsync)
            .WithName("ExecuteWorkflow")
            .WithDescription("Executes a workflow and streams events via SSE")
            .Accepts<WorkflowExecutionRequest>(MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/resume", ResumeWorkflowAsync)
            .WithName("ResumeWorkflow")
            .WithDescription("Resumes a paused workflow with a signal")
            .Accepts<WorkflowResumeRequest>(MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapGet("/workflows", GetAvailableWorkflowsAsync)
            .WithName("GetAvailableWorkflows")
            .WithDescription("Gets the list of available workflow definitions")
            .Produces<IReadOnlyList<WorkflowDefinitionInfo>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> ExecuteWorkflowAsync(
        [FromBody] WorkflowExecutionRequest request,
        [FromServices] WorkflowHostService workflowHost,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "RunId is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.WorkflowName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "WorkflowName is required"
            });
        }

        // Set up SSE response
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        await foreach (var evt in workflowHost.ExecuteAsync(request, cancellationToken))
        {
            await WriteEventAsync(httpContext.Response, evt, cancellationToken);
        }

        return Results.Empty;
    }

    private static async Task<IResult> ResumeWorkflowAsync(
        [FromBody] WorkflowResumeRequest request,
        [FromServices] WorkflowHostService workflowHost,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "RunId is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.WorkflowName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "WorkflowName is required"
            });
        }

        // Set up SSE response
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        await foreach (var evt in workflowHost.ResumeAsync(request, cancellationToken))
        {
            await WriteEventAsync(httpContext.Response, evt, cancellationToken);
        }

        return Results.Empty;
    }

    private static async Task<Ok<IReadOnlyList<WorkflowDefinitionInfo>>> GetAvailableWorkflowsAsync(
        [FromServices] WorkflowHostService workflowHost,
        CancellationToken cancellationToken)
    {
        var workflows = await workflowHost.GetAvailableWorkflowsAsync(cancellationToken);
        return TypedResults.Ok(workflows);
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        WorkflowStatusEvent evt,
        CancellationToken cancellationToken)
    {
        var eventType = evt switch
        {
            WorkflowStartedEvent => "workflow.started",
            WorkflowStepStartedEvent => "step.started",
            WorkflowStepCompletedEvent => "step.completed",
            WorkflowSignalRequestedEvent => "signal.requested",
            WorkflowSignalReceivedEvent => "signal.received",
            WorkflowArtifactCreatedEvent => "artifact.created",
            WorkflowCompletedSignalEvent => "workflow.completed.signal",
            WorkflowCompletedEvent => "workflow.completed",
            WorkflowFailedEvent => "workflow.failed",
            WorkflowCancelledEvent => "workflow.cancelled",
            WorkflowAbortedEvent => "workflow.aborted",
            _ => "unknown"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(evt, AgentContractsJsonUtilities.DefaultOptions);

        await response.WriteAsync($"event: {eventType}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
