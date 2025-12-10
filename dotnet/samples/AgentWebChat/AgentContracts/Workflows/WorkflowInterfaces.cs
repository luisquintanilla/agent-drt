// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentContracts.Workflows;

/// <summary>
/// Client interface for interacting with workflows.
/// Implemented by HTTP client in frontend, backed by Gateway HTTP API.
/// </summary>
public interface IWorkflowClient
{
    /// <summary>
    /// Starts a new workflow execution.
    /// </summary>
    Task<WorkflowRun> StartWorkflowAsync(
        StartWorkflowRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of a workflow run.
    /// </summary>
    Task<WorkflowRun?> GetWorkflowAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists workflow runs with optional filtering.
    /// </summary>
    Task<WorkflowListResponse<WorkflowRunSummary>> ListWorkflowsAsync(
        ListWorkflowsRequest? request = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a signal to a workflow waiting at a RequestPort.
    /// </summary>
    Task<WorkflowRun> SendSignalAsync(
        string runId,
        WorkflowSignal signal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cooperative cancellation of a workflow.
    /// The workflow will complete its current step and clean up gracefully.
    /// </summary>
    Task<WorkflowRun> CancelWorkflowAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forcefully aborts a workflow immediately (administrative action).
    /// </summary>
    Task<WorkflowRun> AbortWorkflowAsync(
        string runId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams workflow events in real-time (SSE).
    /// </summary>
    IAsyncEnumerable<WorkflowStatusEvent> StreamEventsAsync(
        string runId,
        int? startingAfter = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for hosting and executing workflows.
/// Implemented by AgentHost, called by Gateway via HTTP.
/// </summary>
public interface IWorkflowHost
{
    /// <summary>
    /// Executes a workflow and streams events.
    /// </summary>
    IAsyncEnumerable<WorkflowStatusEvent> ExecuteAsync(
        WorkflowExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused workflow by sending a signal.
    /// </summary>
    IAsyncEnumerable<WorkflowStatusEvent> ResumeAsync(
        WorkflowResumeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of available workflow definitions.
    /// </summary>
    Task<IReadOnlyList<WorkflowDefinitionInfo>> GetAvailableWorkflowsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Callback interface for AgentHost to persist workflow state in Gateway.
/// Uses ETags for optimistic concurrency control.
/// </summary>
public interface IWorkflowStateService
{
    /// <summary>
    /// Updates the workflow run status.
    /// </summary>
    /// <param name="runId">The workflow run ID.</param>
    /// <param name="update">The status update.</param>
    /// <param name="etag">Expected ETag for optimistic concurrency. Null to skip check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new ETag after the update.</returns>
    /// <exception cref="WorkflowConcurrencyException">Thrown if ETag doesn't match.</exception>
    Task<string> UpdateStatusAsync(
        string runId,
        WorkflowRunStatusUpdate update,
        string? etag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a workflow step started.
    /// </summary>
    Task<string> RecordStepStartedAsync(
        string runId,
        WorkflowStepStartedRecord step,
        string? etag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a workflow step completed.
    /// </summary>
    Task<string> RecordStepCompletedAsync(
        string runId,
        WorkflowStepCompletedRecord step,
        string? etag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the workflow is waiting for external input at a RequestPort.
    /// </summary>
    Task<string> RecordPendingRequestAsync(
        string runId,
        PendingExternalRequest request,
        string? etag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears a pending request when it's been responded to.
    /// </summary>
    Task<string> ClearPendingRequestAsync(
        string runId,
        string requestId,
        string? etag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a checkpoint for workflow resumption.
    /// </summary>
    Task<string> SaveCheckpointAsync(
        string runId,
        WorkflowCheckpointData checkpoint,
        string? etag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the latest checkpoint.
    /// </summary>
    /// <returns>The checkpoint data and current ETag, or null if not found.</returns>
    Task<WorkflowCheckpointResult?> GetCheckpointAsync(
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an output artifact from the workflow.
    /// </summary>
    Task<string> RecordArtifactAsync(
        string runId,
        WorkflowArtifactRecord artifact,
        string? etag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current ETag for a workflow run.
    /// </summary>
    Task<string?> GetETagAsync(
        string runId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a checkpoint retrieval.
/// </summary>
public sealed class WorkflowCheckpointResult
{
    public required WorkflowCheckpointData Checkpoint { get; init; }
    public required string ETag { get; init; }
}

/// <summary>
/// Exception thrown when an ETag mismatch occurs.
/// </summary>
public class WorkflowConcurrencyException : Exception
{
    public string? RunId { get; }
    public string? ExpectedETag { get; }
    public string? ActualETag { get; }

    public WorkflowConcurrencyException()
    {
    }

    public WorkflowConcurrencyException(string message)
        : base(message)
    {
    }

    public WorkflowConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public WorkflowConcurrencyException(string runId, string? expectedETag, string? actualETag)
        : base($"Concurrency conflict for workflow '{runId}'. Expected ETag '{expectedETag}', but was '{actualETag}'.")
    {
        this.RunId = runId;
        this.ExpectedETag = expectedETag;
        this.ActualETag = actualETag;
    }
}

/// <summary>
/// Exception thrown when a workflow is not found.
/// </summary>
public class WorkflowNotFoundException : Exception
{
    public string? RunId { get; init; }

    public WorkflowNotFoundException()
    {
    }

    public WorkflowNotFoundException(string message)
        : base(message)
    {
    }

    public WorkflowNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static WorkflowNotFoundException ForRunId(string runId)
    {
        return new WorkflowNotFoundException($"Workflow '{runId}' not found.")
        {
            RunId = runId
        };
    }
}
