// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using AgentContracts.Workflows;
using Orleans;

namespace AgentGateway.Workflows;

/// <summary>
/// Persistent state for a workflow grain.
/// </summary>
[GenerateSerializer]
internal sealed class WorkflowGrainState
{
    /// <summary>
    /// The workflow run data.
    /// </summary>
    [Id(0)]
    public WorkflowRun? Run { get; set; }

    /// <summary>
    /// The streaming events collected during workflow execution.
    /// </summary>
    [Id(1)]
    public List<WorkflowStatusEvent> Events { get; set; } = [];

    /// <summary>
    /// The latest checkpoint for workflow resumption.
    /// </summary>
    [Id(2)]
    public WorkflowCheckpointData? Checkpoint { get; set; }

    /// <summary>
    /// Version number incremented on each state change.
    /// Used as the backing value for the opaque ETag string.
    /// </summary>
    [Id(3)]
    public long Version { get; set; }

    /// <summary>
    /// Whether cooperative cancellation has been requested.
    /// </summary>
    [Id(4)]
    public bool CancellationRequested { get; set; }

    /// <summary>
    /// The ID of the worker assigned to execute this workflow.
    /// Used to route resume requests to the same worker for state consistency.
    /// </summary>
    [Id(5)]
    public string? AssignedWorkerId { get; set; }

    /// <summary>
    /// Gets the ETag as an opaque string (hex-encoded version number).
    /// </summary>
    public string GetETag() => this.Version.ToString("x16");

    /// <summary>
    /// Validates the expected ETag and increments the version.
    /// </summary>
    /// <param name="expectedETag">The expected ETag, or null to skip validation.</param>
    /// <param name="runId">The workflow run ID for error messages.</param>
    /// <exception cref="WorkflowConcurrencyException">Thrown if ETag doesn't match.</exception>
    public void IncrementVersion(string? expectedETag, string runId)
    {
        if (expectedETag != null && expectedETag != this.GetETag())
        {
            throw new WorkflowConcurrencyException(runId, expectedETag, this.GetETag());
        }

        this.Version++;
    }
}
