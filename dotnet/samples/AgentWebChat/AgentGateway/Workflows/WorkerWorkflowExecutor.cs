// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts.Telemetry;
using AgentContracts.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentGateway.Workflows;

/// <summary>
/// Executor that forwards workflow execution requests to remote AgentHost workers via HTTP.
/// Uses the worker registry and discovery cache to route requests to appropriate workers.
/// </summary>
internal sealed class WorkerWorkflowExecutor : IWorkflowExecutor
{
    private readonly WorkerRegistry _registry;
    private readonly WorkerDiscoveryCache _cache;
    private readonly HttpClient _httpClient;
    private readonly AgentGatewayOptions _options;
    private readonly ILogger<WorkerWorkflowExecutor> _logger;

    public WorkerWorkflowExecutor(
        WorkerRegistry registry,
        WorkerDiscoveryCache cache,
        IHttpClientFactory httpClientFactory,
        IOptions<AgentGatewayOptions> options,
        ILogger<WorkerWorkflowExecutor> logger)
    {
        this._registry = registry;
        this._cache = cache;
        this._httpClient = httpClientFactory.CreateClient();
        this._options = options.Value;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public async Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowExecutionRequest request,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = WorkflowActivitySource.StartWorkflowExecution(request.RunId, request.WorkflowName, ActivityKind.Client);

        this._logger.LogInformation(
            "Dispatching workflow execution: RunId={RunId}, WorkflowName={WorkflowName}",
            request.RunId, request.WorkflowName);

        // Select a worker that supports this workflow
        var worker = await this.SelectWorkerForWorkflowAsync(request.WorkflowName, preferredWorkerId, cancellationToken);
        if (worker is null)
        {
            this._logger.LogError("No available worker supports workflow '{WorkflowName}'", request.WorkflowName);
            WorkflowActivitySource.RecordException(activity, new InvalidOperationException($"No available worker supports workflow '{request.WorkflowName}'"));
            return new WorkflowExecutionResult
            {
                Success = false,
                ErrorCode = "NO_WORKER_AVAILABLE",
                ErrorMessage = $"No available worker supports workflow '{request.WorkflowName}'"
            };
        }

        // Add worker info to activity
        activity?.SetTag(TelemetryConstants.WorkerId, worker.Id);
        activity?.SetTag(TelemetryConstants.WorkerAddress, worker.Endpoint.ToString());

        // Build the request URL - workers should have a /v1/workflow-host/execute endpoint
        var workerEndpoint = new Uri(worker.Endpoint, "/v1/workflow-host/execute");

        try
        {
            // Create HTTP request with the WorkflowExecutionRequest body
            var json = JsonSerializer.Serialize(request, AgentGatewayJsonUtilities.DefaultOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, workerEndpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            this._logger.LogInformation(
                "Forwarding workflow {RunId} to worker {WorkerId} at {WorkerEndpoint}",
                request.RunId, worker.Id, workerEndpoint);

            // Send the request and wait for completion
            // Note: The AgentHost will callback to update state, so we just need to wait for the SSE stream to complete
            var httpResponse = await this._httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                this._logger.LogError(
                    "Worker {WorkerId} returned error {StatusCode} for workflow {RunId}: {ErrorBody}",
                    worker.Id, httpResponse.StatusCode, request.RunId, errorBody);

                WorkflowActivitySource.RecordHttpResponse(activity, (int)httpResponse.StatusCode);

                return new WorkflowExecutionResult
                {
                    Success = false,
                    WorkerId = worker.Id,
                    ErrorCode = "WORKER_ERROR",
                    ErrorMessage = $"Worker returned {httpResponse.StatusCode}: {errorBody}"
                };
            }

            // Read the SSE stream to completion - events are informational since state updates come via callbacks
            await ConsumeEventStreamAsync(httpResponse, request.RunId, cancellationToken);

            this._logger.LogInformation(
                "Workflow {RunId} execution dispatched successfully to worker {WorkerId}",
                request.RunId, worker.Id);

            WorkflowActivitySource.AddEvent(activity, TelemetryConstants.EventWorkerDispatched,
                (TelemetryConstants.WorkerId, worker.Id),
                (TelemetryConstants.WorkerAddress, worker.Endpoint.ToString()));

            return new WorkflowExecutionResult
            {
                Success = true,
                WorkerId = worker.Id
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to dispatch workflow {RunId} to worker {WorkerId}", request.RunId, worker.Id);
            WorkflowActivitySource.RecordException(activity, ex);
            return new WorkflowExecutionResult
            {
                Success = false,
                WorkerId = worker.Id,
                ErrorCode = "DISPATCH_FAILED",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc/>
    public async Task<WorkflowExecutionResult> ResumeAsync(
        WorkflowResumeRequest request,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = WorkflowActivitySource.StartWorkflowResume(request.RunId, request.WorkflowName, request.Signal.RequestId);

        this._logger.LogInformation(
            "Dispatching workflow resume: RunId={RunId}, WorkflowName={WorkflowName}, SignalRequestId={SignalRequestId}",
            request.RunId, request.WorkflowName, request.Signal.RequestId);

        // Select a worker that supports this workflow, preferring the assigned worker
        var worker = await this.SelectWorkerForWorkflowAsync(request.WorkflowName, preferredWorkerId, cancellationToken);
        if (worker is null)
        {
            this._logger.LogError("No available worker supports workflow '{WorkflowName}'", request.WorkflowName);
            WorkflowActivitySource.RecordException(activity, new InvalidOperationException($"No available worker supports workflow '{request.WorkflowName}'"));
            return new WorkflowExecutionResult
            {
                Success = false,
                ErrorCode = "NO_WORKER_AVAILABLE",
                ErrorMessage = $"No available worker supports workflow '{request.WorkflowName}'"
            };
        }

        // Add worker info to activity
        activity?.SetTag(TelemetryConstants.WorkerId, worker.Id);
        activity?.SetTag(TelemetryConstants.WorkerAddress, worker.Endpoint.ToString());

        // Build the request URL
        var workerEndpoint = new Uri(worker.Endpoint, "/v1/workflow-host/resume");

        try
        {
            var json = JsonSerializer.Serialize(request, AgentGatewayJsonUtilities.DefaultOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, workerEndpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            this._logger.LogInformation(
                "Forwarding workflow resume {RunId} to worker {WorkerId} at {WorkerEndpoint}",
                request.RunId, worker.Id, workerEndpoint);

            var httpResponse = await this._httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                this._logger.LogError(
                    "Worker {WorkerId} returned error {StatusCode} for workflow resume {RunId}: {ErrorBody}",
                    worker.Id, httpResponse.StatusCode, request.RunId, errorBody);

                WorkflowActivitySource.RecordHttpResponse(activity, (int)httpResponse.StatusCode);

                return new WorkflowExecutionResult
                {
                    Success = false,
                    WorkerId = worker.Id,
                    ErrorCode = "WORKER_ERROR",
                    ErrorMessage = $"Worker returned {httpResponse.StatusCode}: {errorBody}"
                };
            }

            // Read the SSE stream to completion
            await ConsumeEventStreamAsync(httpResponse, request.RunId, cancellationToken);

            this._logger.LogInformation(
                "Workflow {RunId} resume dispatched successfully to worker {WorkerId}",
                request.RunId, worker.Id);

            WorkflowActivitySource.AddEvent(activity, TelemetryConstants.EventWorkerDispatched,
                (TelemetryConstants.WorkerId, worker.Id),
                (TelemetryConstants.WorkerAddress, worker.Endpoint.ToString()));

            return new WorkflowExecutionResult
            {
                Success = true,
                WorkerId = worker.Id
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to dispatch workflow resume {RunId} to worker {WorkerId}", request.RunId, worker.Id);
            WorkflowActivitySource.RecordException(activity, ex);
            return new WorkflowExecutionResult
            {
                Success = false,
                WorkerId = worker.Id,
                ErrorCode = "DISPATCH_FAILED",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Select the best available worker that supports the given workflow.
    /// </summary>
    /// <param name="workflowName">The workflow name to match.</param>
    /// <param name="preferredWorkerId">Optional preferred worker ID for sticky routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async ValueTask<WorkerRegistry.WorkerInfo?> SelectWorkerForWorkflowAsync(
        string workflowName,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default)
    {
        // If a preferred worker is specified and available, try to use it
        if (!string.IsNullOrEmpty(preferredWorkerId))
        {
            var preferredWorker = this._registry.ActiveWorkers
                .FirstOrDefault(w => string.Equals(w.Id, preferredWorkerId, StringComparison.Ordinal));

            if (preferredWorker is not null)
            {
                var supportsWorkflow = await this.WorkerSupportsWorkflowAsync(preferredWorker, workflowName, cancellationToken);
                if (supportsWorkflow)
                {
                    this._logger.LogDebug(
                        "Using preferred worker {WorkerId} for workflow {WorkflowName}",
                        preferredWorkerId, workflowName);
                    return preferredWorker;
                }
            }
        }

        // Query each worker's discovery endpoint to find one that supports the workflow
        // Workflows are exposed alongside agents in the entities endpoint
        foreach (var worker in this._registry.ActiveWorkers)
        {
            // Check if worker has a workflows endpoint
            // For now, we check if the worker supports the workflow by querying its workflow-host/workflows endpoint
            var supportsWorkflow = await this.WorkerSupportsWorkflowAsync(worker, workflowName, cancellationToken);
            if (supportsWorkflow)
            {
                return worker;
            }
        }

        // Fall back to default worker if available
        if (this._registry.DefaultWorker is not null)
        {
            return this._registry.DefaultWorker;
        }

        return null;
    }

    /// <summary>
    /// Checks if a worker supports a specific workflow by querying its workflow definitions endpoint.
    /// </summary>
    private async ValueTask<bool> WorkerSupportsWorkflowAsync(
        WorkerRegistry.WorkerInfo worker,
        string workflowName,
        CancellationToken cancellationToken)
    {
        try
        {
            var workflowsUri = new Uri(worker.Endpoint, "/v1/workflow-host/workflows");
            var response = await this._httpClient.GetAsync(workflowsUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var workflows = await response.Content.ReadFromJsonAsync<List<WorkflowDefinitionInfo>>(
                AgentGatewayJsonUtilities.DefaultOptions,
                cancellationToken);

            return workflows?.Exists(w => string.Equals(w.Name, workflowName, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to query workflows from worker {WorkerId}", worker.Id);
            return false;
        }
    }

    /// <summary>
    /// Consumes the SSE event stream from the worker.
    /// Events are primarily informational since state updates come via callbacks.
    /// </summary>
    private async Task ConsumeEventStreamAsync(
        HttpResponseMessage response,
        string runId,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                var eventType = line.Substring(7);
                this._logger.LogDebug("Workflow {RunId} received event: {EventType}", runId, eventType);
            }
            // We don't need to process the data since state updates come via callbacks
        }
    }
}

/// <summary>
/// Interface for dispatching workflow execution to workers.
/// </summary>
public interface IWorkflowExecutor
{
    /// <summary>
    /// Dispatches a workflow execution request to a worker.
    /// </summary>
    /// <param name="request">The execution request.</param>
    /// <param name="preferredWorkerId">Optional preferred worker ID (for sticky routing).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowExecutionRequest request,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a workflow resume request to a worker.
    /// </summary>
    /// <param name="request">The resume request.</param>
    /// <param name="preferredWorkerId">Optional preferred worker ID (for sticky routing).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkflowExecutionResult> ResumeAsync(
        WorkflowResumeRequest request,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a workflow execution dispatch operation.
/// </summary>
public sealed class WorkflowExecutionResult
{
    /// <summary>
    /// Whether the dispatch was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The ID of the worker that handled the execution (if successful).
    /// </summary>
    public string? WorkerId { get; init; }

    /// <summary>
    /// Error code if the dispatch failed.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Error message if the dispatch failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
