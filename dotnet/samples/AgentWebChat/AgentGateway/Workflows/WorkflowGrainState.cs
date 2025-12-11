// Copyright (c) Microsoft. All rights reserved.

using System;
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
    /// Tracks the current execution state for retry support.
    /// When a grain wakes up via reminder, this helps determine what action to take.
    /// </summary>
    [Id(6)]
    public WorkflowExecutionState ExecutionState { get; set; } = WorkflowExecutionState.NotStarted;

    /// <summary>
    /// The number of retry attempts that have been made.
    /// Used to implement exponential backoff and prevent infinite retries.
    /// </summary>
    [Id(7)]
    public int RetryCount { get; set; }

    /// <summary>
    /// The timestamp of the last execution attempt.
    /// Used to calculate backoff delays for retries.
    /// </summary>
    [Id(8)]
    public DateTimeOffset? LastExecutionAttempt { get; set; }

    /// <summary>
    /// The pending signal that needs to be processed after a crash recovery.
    /// When a signal is received but the workflow hasn't resumed yet, this stores the signal.
    /// </summary>
    [Id(9)]
    public WorkflowSignal? PendingSignal { get; set; }

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

/// <summary>
/// Tracks the execution state of a workflow for retry and recovery support.
/// </summary>
[GenerateSerializer]
public enum WorkflowExecutionState
{
    /// <summary>
    /// Workflow has not started execution yet.
    /// </summary>
    NotStarted = 0,

    /// <summary>
    /// Workflow execution has been dispatched to a worker.
    /// If the grain wakes up in this state, it should check if the worker is still running.
    /// </summary>
    Dispatched = 1,

    /// <summary>
    /// Workflow is actively executing on a worker.
    /// The worker is sending state updates via callbacks.
    /// </summary>
    Executing = 2,

    /// <summary>
    /// Workflow is paused waiting for an external signal (HITL).
    /// No active execution happening.
    /// </summary>
    WaitingForSignal = 3,

    /// <summary>
    /// A signal was received and resume has been dispatched.
    /// Similar to Dispatched but for resume operations.
    /// </summary>
    ResumeDispatched = 4,

    /// <summary>
    /// Workflow is actively resuming after a signal.
    /// </summary>
    Resuming = 5,

    /// <summary>
    /// Workflow has completed (terminal state).
    /// </summary>
    Completed = 6,

    /// <summary>
    /// Workflow has failed (terminal state).
    /// </summary>
    Failed = 7
}
