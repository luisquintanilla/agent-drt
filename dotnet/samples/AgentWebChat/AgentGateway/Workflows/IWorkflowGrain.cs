// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts.Workflows;
using Orleans;

namespace AgentGateway.Workflows;

/// <summary>
/// Orleans grain interface for managing a single workflow run.
/// </summary>
internal interface IWorkflowGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates and starts a new workflow run.
    /// </summary>
    Task<WorkflowRun> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current state of the workflow run.
    /// </summary>
    Task<WorkflowRun?> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends a signal to the workflow (response to a pending request).
    /// </summary>
    Task<WorkflowRun> SendSignalAsync(WorkflowSignal signal, CancellationToken cancellationToken);

    /// <summary>
    /// Requests cooperative cancellation of the workflow.
    /// </summary>
    Task<WorkflowRun> CancelAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Forcefully aborts the workflow.
    /// </summary>
    Task<WorkflowRun> AbortAsync(string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Streams workflow events starting from the specified sequence number.
    /// </summary>
    IAsyncEnumerable<WorkflowStatusEvent> StreamEventsAsync(int? startingAfter, CancellationToken cancellationToken);

    // ============ State Service Methods (called by AgentHost) ============

    /// <summary>
    /// Updates the workflow status.
    /// </summary>
    Task<string> UpdateStatusAsync(WorkflowRunStatusUpdate update, string? etag, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a step started.
    /// </summary>
    Task<string> RecordStepStartedAsync(WorkflowStepStartedRecord step, string? etag, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a step completed.
    /// </summary>
    Task<string> RecordStepCompletedAsync(WorkflowStepCompletedRecord step, string? etag, CancellationToken cancellationToken);

    /// <summary>
    /// Records a pending external request.
    /// </summary>
    Task<string> RecordPendingRequestAsync(PendingExternalRequest request, string? etag, CancellationToken cancellationToken);

    /// <summary>
    /// Clears a pending request.
    /// </summary>
    Task<string> ClearPendingRequestAsync(string requestId, string? etag, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a checkpoint.
    /// </summary>
    Task<string> SaveCheckpointAsync(WorkflowCheckpointData checkpoint, string? etag, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current checkpoint.
    /// </summary>
    Task<WorkflowCheckpointResult?> GetCheckpointAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records an artifact.
    /// </summary>
    Task<string> RecordArtifactAsync(WorkflowArtifactRecord artifact, string? etag, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current ETag.
    /// </summary>
    Task<string?> GetETagAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the ID of the worker assigned to execute this workflow.
    /// </summary>
    Task<string?> GetAssignedWorkerIdAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Assigns a worker to execute this workflow.
    /// </summary>
    Task SetAssignedWorkerIdAsync(string workerId, CancellationToken cancellationToken);
}
