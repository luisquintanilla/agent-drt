// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts.Telemetry;
using AgentContracts.Workflows;
using AgentGateway.Utilities;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace AgentGateway.Workflows;

/// <summary>
/// Orleans grain implementation for managing a single workflow run.
/// Handles state persistence, SSE streaming, and Orleans Reminders for reliability.
/// Implements retry logic to recover from crashes and drive workflows to completion.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class WorkflowGrain(
    [PersistentState("state")] IPersistentState<WorkflowGrainState> workflowState,
    IGrainFactory grainFactory,
    IWorkflowExecutor workflowExecutor,
    ILogger<WorkflowGrain> logger) : Grain, IWorkflowGrain, IRemindable, IDisposable
{
    private const string ExecutionReminderName = "WorkflowExecution";
    private const int MaxRetryCount = 5;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(10);

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AsyncManualResetEvent _stateUpdatedEvent = new();

    private string RunId => this.GetPrimaryKeyString();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // If we have a workflow that's not completed, ensure reminder is registered
        if (workflowState.State.Run is { } run && !IsTerminalStatus(run.Status))
        {
            await this.RegisterOrUpdateReminder(
                ExecutionReminderName,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));

            // Check if we need to retry execution on activation
            await this.CheckAndRetryExecutionAsync(cancellationToken);
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await this._shutdownCts.CancelAsync();
        this._stateUpdatedEvent.Cancel();
    }

    public async Task<WorkflowRun> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("start", "WorkflowGrain", this.RunId);
        activity?.SetTag(TelemetryConstants.WorkflowName, request.WorkflowName);

        if (workflowState.State.Run is not null)
        {
            throw new InvalidOperationException($"Workflow run '{this.RunId}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        workflowState.State.Run = new WorkflowRun
        {
            Id = this.RunId,
            WorkflowName = request.WorkflowName,
            Status = WorkflowRunStatus.Queued,
            Input = request.Input,
            CreatedAt = now,
            UpdatedAt = now,
            Steps = [],
            Artifacts = [],
            PendingRequests = [],
            Metadata = request.Metadata,
            ETag = null // Will be set after save
        };

        workflowState.State.Version = 1;
        workflowState.State.Events.Clear();
        workflowState.State.ExecutionState = WorkflowExecutionState.NotStarted;
        workflowState.State.RetryCount = 0;
        workflowState.State.LastExecutionAttempt = null;

        // Add started event
        var startedEvent = new WorkflowStartedEvent
        {
            RunId = this.RunId,
            SequenceNumber = 1,
            Timestamp = now,
            WorkflowName = request.WorkflowName,
            Input = request.Input
        };
        workflowState.State.Events.Add(startedEvent);

        await workflowState.WriteStateAsync(cancellationToken);

        // Register with the workflow index
        var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        await indexGrain.RegisterAsync(new WorkflowRunSummary
        {
            Id = this.RunId,
            WorkflowName = request.WorkflowName,
            Status = WorkflowRunStatus.Queued,
            CreatedAt = now,
            PendingRequestCount = 0
        }, cancellationToken);

        // Register reminder for background execution and recovery
        await this.RegisterOrUpdateReminder(
            ExecutionReminderName,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

        this._stateUpdatedEvent.SignalAndReset();

        logger.LogInformation("Workflow '{RunId}' started with workflow name '{WorkflowName}'", this.RunId, request.WorkflowName);

        WorkflowActivitySource.AddEvent(activity, TelemetryConstants.EventWorkflowStateChanged,
            (TelemetryConstants.WorkflowStatus, WorkflowRunStatus.Queued.ToString()));

        return this.GetRunWithETag();
    }

    public Task<WorkflowRun?> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.Run is not null ? this.GetRunWithETag() : null);
    }

    public async Task<WorkflowRun> SendSignalAsync(WorkflowSignal signal, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartSignalDelivery(this.RunId, signal.RequestId);

        this.EnsureRunExists();

        var pendingRequest = workflowState.State.Run!.PendingRequests.FirstOrDefault(r => r.RequestId == signal.RequestId);
        if (pendingRequest is null)
        {
            throw new InvalidOperationException($"No pending request with ID '{signal.RequestId}' found for workflow '{this.RunId}'.");
        }

        // Remove the pending request
        workflowState.State.Run = workflowState.State.Run with
        {
            PendingRequests = workflowState.State.Run.PendingRequests.Where(r => r.RequestId != signal.RequestId).ToList(),
            Status = WorkflowRunStatus.Running,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Store the pending signal for retry support
        workflowState.State.PendingSignal = signal;
        workflowState.State.ExecutionState = WorkflowExecutionState.ResumeDispatched;
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;

        // Add signal received event
        var signalEvent = new WorkflowSignalReceivedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = DateTimeOffset.UtcNow,
            RequestId = signal.RequestId,
            Response = signal.Response
        };
        workflowState.State.Events.Add(signalEvent);

        workflowState.State.IncrementVersion(null, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        this._stateUpdatedEvent.SignalAndReset();

        logger.LogInformation("Workflow '{RunId}' received signal for request '{RequestId}'", this.RunId, signal.RequestId);

        WorkflowActivitySource.AddEvent(activity, TelemetryConstants.EventSignalReceived,
            (TelemetryConstants.SignalRequestId, signal.RequestId));

        return this.GetRunWithETag();
    }

    public async Task<WorkflowRun> CancelAsync(CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("cancel", "WorkflowGrain", this.RunId);

        this.EnsureRunExists();

        if (IsTerminalStatus(workflowState.State.Run!.Status))
        {
            throw new InvalidOperationException($"Cannot cancel workflow '{this.RunId}' - it is already in terminal status '{workflowState.State.Run.Status}'.");
        }

        workflowState.State.CancellationRequested = true;
        workflowState.State.Run = workflowState.State.Run with
        {
            Status = WorkflowRunStatus.Cancelling,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        workflowState.State.IncrementVersion(null, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        this._stateUpdatedEvent.SignalAndReset();

        logger.LogInformation("Workflow '{RunId}' cancellation requested", this.RunId);

        return this.GetRunWithETag();
    }

    public async Task<WorkflowRun> AbortAsync(string reason, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("abort", "WorkflowGrain", this.RunId);
        activity?.SetTag("abort.reason", reason);

        this.EnsureRunExists();

        if (IsTerminalStatus(workflowState.State.Run!.Status))
        {
            throw new InvalidOperationException($"Cannot abort workflow '{this.RunId}' - it is already in terminal status '{workflowState.State.Run.Status}'.");
        }

        var now = DateTimeOffset.UtcNow;
        workflowState.State.Run = workflowState.State.Run with
        {
            Status = WorkflowRunStatus.Aborted,
            UpdatedAt = now,
            CompletedAt = now
        };

        workflowState.State.ExecutionState = WorkflowExecutionState.Failed;
        workflowState.State.PendingSignal = null;

        // Add aborted event
        var abortedEvent = new WorkflowAbortedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = now,
            Reason = reason
        };
        workflowState.State.Events.Add(abortedEvent);

        workflowState.State.IncrementVersion(null, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        await this.UnregisterReminderIfExistsAsync();
        this._stateUpdatedEvent.SignalAndReset();

        logger.LogWarning("Workflow '{RunId}' aborted: {Reason}", this.RunId, reason);

        return this.GetRunWithETag();
    }

    public async IAsyncEnumerable<WorkflowStatusEvent> StreamEventsAsync(
        int? startingAfter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (workflowState.State.Run is null)
        {
            yield break;
        }

        var streamedCount = startingAfter ?? 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Yield any available updates from the current position
            while (streamedCount < workflowState.State.Events.Count)
            {
                yield return workflowState.State.Events[streamedCount];
                streamedCount++;
            }

            // Check if we're done (workflow in terminal state)
            if (workflowState.State.Run is { } run && IsTerminalStatus(run.Status))
            {
                break;
            }

            // Wait for more updates
            await this._stateUpdatedEvent.WaitAsync(cancellationToken);
        }
    }

    // ============ State Service Methods ============

    public async Task<string> UpdateStatusAsync(WorkflowRunStatusUpdate update, string? etag, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartStatusUpdate(
            this.RunId,
            workflowState.State.Run?.Status.ToString() ?? "Unknown",
            update.Status.ToString());

        this.EnsureRunExists();

        var now = DateTimeOffset.UtcNow;
        workflowState.State.Run = workflowState.State.Run! with
        {
            Status = update.Status,
            Error = update.Error,
            UpdatedAt = now,
            CompletedAt = IsTerminalStatus(update.Status) ? now : workflowState.State.Run.CompletedAt
        };

        // Update execution state based on status
        workflowState.State.ExecutionState = update.Status switch
        {
            WorkflowRunStatus.Running => WorkflowExecutionState.Executing,
            WorkflowRunStatus.WaitingForSignal => WorkflowExecutionState.WaitingForSignal,
            WorkflowRunStatus.Completed => WorkflowExecutionState.Completed,
            WorkflowRunStatus.Failed => WorkflowExecutionState.Failed,
            WorkflowRunStatus.Cancelled => WorkflowExecutionState.Completed,
            WorkflowRunStatus.Aborted => WorkflowExecutionState.Failed,
            _ => workflowState.State.ExecutionState
        };

        // Clear pending signal if we've reached a terminal or waiting state
        if (update.Status is WorkflowRunStatus.Completed or WorkflowRunStatus.Failed
            or WorkflowRunStatus.Cancelled or WorkflowRunStatus.Aborted
            or WorkflowRunStatus.WaitingForSignal)
        {
            workflowState.State.PendingSignal = null;
        }

        workflowState.State.IncrementVersion(etag, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);

        // Update the workflow index
        var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        await indexGrain.UpdateAsync(this.RunId, update.Status, workflowState.State.Run.PendingRequests.Count, cancellationToken);

        if (IsTerminalStatus(update.Status))
        {
            await this.UnregisterReminderIfExistsAsync();
        }

        this._stateUpdatedEvent.SignalAndReset();

        logger.LogDebug("Workflow '{RunId}' status updated to '{Status}'", this.RunId, update.Status);

        return workflowState.State.GetETag();
    }

    public async Task<string> RecordStepStartedAsync(WorkflowStepStartedRecord step, string? etag, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartStepExecution(this.RunId, step.StepId, step.ExecutorId, step.ExecutorName);

        this.EnsureRunExists();

        var stepInfo = new WorkflowStepInfo
        {
            StepId = step.StepId,
            ExecutorId = step.ExecutorId,
            ExecutorName = step.ExecutorName,
            StartedAt = step.StartedAt
        };

        workflowState.State.Run = workflowState.State.Run! with
        {
            Steps = [.. workflowState.State.Run.Steps, stepInfo],
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Mark as executing since we're receiving worker updates
        workflowState.State.ExecutionState = WorkflowExecutionState.Executing;
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;

        // Add event
        var evt = new WorkflowStepStartedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = step.StartedAt,
            Step = step
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.IncrementVersion(etag, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    public async Task<string> RecordStepCompletedAsync(WorkflowStepCompletedRecord step, string? etag, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("record_step_completed", "WorkflowGrain", this.RunId);
        activity?.SetTag(TelemetryConstants.StepId, step.StepId);
        activity?.SetTag(TelemetryConstants.StepDurationMs, step.DurationMs);

        this.EnsureRunExists();

        // Find and update the step
        var steps = workflowState.State.Run!.Steps.ToList();
        var existingStepIndex = steps.FindIndex(s => s.StepId == step.StepId);
        if (existingStepIndex >= 0)
        {
            steps[existingStepIndex] = steps[existingStepIndex] with
            {
                CompletedAt = step.CompletedAt,
                Output = step.Output,
                DurationMs = step.DurationMs
            };
        }

        workflowState.State.Run = workflowState.State.Run with
        {
            Steps = steps,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Update last execution attempt to track activity
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;

        // Add event
        var evt = new WorkflowStepCompletedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = step.CompletedAt,
            Step = step
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.IncrementVersion(etag, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    public async Task<string> RecordPendingRequestAsync(PendingExternalRequest request, string? etag, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartSignalRequest(this.RunId, request.RequestId, request.PortId);

        this.EnsureRunExists();

        workflowState.State.Run = workflowState.State.Run! with
        {
            PendingRequests = [.. workflowState.State.Run.PendingRequests, request],
            Status = WorkflowRunStatus.WaitingForSignal,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Update execution state - workflow is now waiting, not actively executing
        workflowState.State.ExecutionState = WorkflowExecutionState.WaitingForSignal;
        workflowState.State.PendingSignal = null; // Clear any pending signal

        // Add event
        var evt = new WorkflowSignalRequestedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = request.RequestedAt,
            Request = request
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.IncrementVersion(etag, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        this._stateUpdatedEvent.SignalAndReset();

        // Update the workflow index
        var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        await indexGrain.UpdateAsync(this.RunId, WorkflowRunStatus.WaitingForSignal, workflowState.State.Run.PendingRequests.Count, cancellationToken);

        logger.LogInformation("Workflow '{RunId}' waiting for signal on request '{RequestId}' (port: {PortId})",
            this.RunId, request.RequestId, request.PortId);

        return workflowState.State.GetETag();
    }

    public async Task<string> ClearPendingRequestAsync(string requestId, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        workflowState.State.Run = workflowState.State.Run! with
        {
            PendingRequests = workflowState.State.Run.PendingRequests.Where(r => r.RequestId != requestId).ToList(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        workflowState.State.IncrementVersion(etag, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    public async Task<string> SaveCheckpointAsync(WorkflowCheckpointData checkpoint, string? etag, CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartSaveCheckpoint(this.RunId, checkpoint.CheckpointId);

        this.EnsureRunExists();

        workflowState.State.Checkpoint = checkpoint;

        workflowState.State.IncrementVersion(etag, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);

        logger.LogDebug("Workflow '{RunId}' checkpoint saved: {CheckpointId}", this.RunId, checkpoint.CheckpointId);

        return workflowState.State.GetETag();
    }

    public Task<WorkflowCheckpointResult?> GetCheckpointAsync(CancellationToken cancellationToken)
    {
        if (workflowState.State.Checkpoint is null)
        {
            return Task.FromResult<WorkflowCheckpointResult?>(null);
        }

        return Task.FromResult<WorkflowCheckpointResult?>(new WorkflowCheckpointResult
        {
            Checkpoint = workflowState.State.Checkpoint,
            ETag = workflowState.State.GetETag()
        });
    }

    public async Task<string> RecordArtifactAsync(WorkflowArtifactRecord artifact, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        workflowState.State.Run = workflowState.State.Run! with
        {
            Artifacts = [.. workflowState.State.Run.Artifacts, artifact],
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Add event
        var evt = new WorkflowArtifactCreatedEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = artifact.CreatedAt,
            Artifact = artifact
        };
        workflowState.State.Events.Add(evt);

        workflowState.State.IncrementVersion(etag, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        this._stateUpdatedEvent.SignalAndReset();

        logger.LogInformation("Workflow '{RunId}' artifact created: {ArtifactName}", this.RunId, artifact.Name);

        return workflowState.State.GetETag();
    }

    public Task<string?> GetETagAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.Run is not null ? workflowState.State.GetETag() : null);
    }

    public Task<string?> GetAssignedWorkerIdAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.AssignedWorkerId);
    }

    public async Task SetAssignedWorkerIdAsync(string workerId, CancellationToken cancellationToken)
    {
        workflowState.State.AssignedWorkerId = workerId;
        await workflowState.WriteStateAsync(cancellationToken);
        logger.LogDebug("Workflow '{RunId}' assigned to worker '{WorkerId}'", this.RunId, workerId);
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("delete", "WorkflowGrain", this.RunId);

        this.EnsureRunExists();

        // Only allow deletion of workflows in terminal status
        if (!IsTerminalStatus(workflowState.State.Run!.Status))
        {
            throw new InvalidOperationException(
                $"Cannot delete workflow '{this.RunId}' - it is in status '{workflowState.State.Run.Status}'. " +
                "Only workflows in terminal status (Completed, Cancelled, Aborted, Failed) can be deleted.");
        }

        // Remove from the workflow index
        var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        await indexGrain.RemoveAsync(this.RunId, cancellationToken);

        // Unregister any reminders
        await this.UnregisterReminderIfExistsAsync();

        // Clear the persistent state
        await workflowState.ClearStateAsync(cancellationToken);

        logger.LogInformation("Workflow '{RunId}' deleted", this.RunId);

        // Deactivate the grain
        this.DeactivateOnIdle();
    }

    // ============ Execution State Methods ============

    public async Task SetExecutionStateAsync(WorkflowExecutionState state, CancellationToken cancellationToken)
    {
        workflowState.State.ExecutionState = state;
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;
        await workflowState.WriteStateAsync(cancellationToken);
        logger.LogDebug("Workflow '{RunId}' execution state set to '{State}'", this.RunId, state);
    }

    public Task<WorkflowExecutionState> GetExecutionStateAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.ExecutionState);
    }

    public async Task SetPendingSignalAsync(WorkflowSignal? signal, CancellationToken cancellationToken)
    {
        workflowState.State.PendingSignal = signal;
        await workflowState.WriteStateAsync(cancellationToken);
    }

    public Task<WorkflowSignal?> GetPendingSignalAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.PendingSignal);
    }

    // ============ Streaming Output Methods ============

    public async Task<string> RecordOutputDeltaAsync(WorkflowOutputDelta delta, string? etag, CancellationToken cancellationToken)
    {
        this.EnsureRunExists();

        // Add streaming event - these are lightweight and streamed in real-time
        var evt = new WorkflowOutputDeltaEvent
        {
            RunId = this.RunId,
            SequenceNumber = workflowState.State.Events.Count + 1,
            Timestamp = DateTimeOffset.UtcNow,
            Delta = delta
        };
        workflowState.State.Events.Add(evt);

        // Update last execution attempt
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;

        workflowState.State.IncrementVersion(etag, this.RunId);
        await workflowState.WriteStateAsync(cancellationToken);
        this._stateUpdatedEvent.SignalAndReset();

        return workflowState.State.GetETag();
    }

    // ============ IRemindable ============

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != ExecutionReminderName)
        {
            return;
        }

        logger.LogDebug("Workflow '{RunId}' reminder tick (execution state: {State}, retry count: {RetryCount})",
            this.RunId, workflowState.State.ExecutionState, workflowState.State.RetryCount);

        // If workflow is in terminal state, unregister the reminder
        if (workflowState.State.Run is { } run && IsTerminalStatus(run.Status))
        {
            await this.UnregisterReminderIfExistsAsync();
            return;
        }

        // Check if we need to retry execution
        await this.CheckAndRetryExecutionAsync(CancellationToken.None);
    }

    // ============ Retry Logic ============

    private async Task CheckAndRetryExecutionAsync(CancellationToken cancellationToken)
    {
        if (workflowState.State.Run is null)
        {
            return;
        }

        var run = workflowState.State.Run;
        var execState = workflowState.State.ExecutionState;
        var lastAttempt = workflowState.State.LastExecutionAttempt;
        var retryCount = workflowState.State.RetryCount;

        // Don't retry if already at max retries
        if (retryCount >= MaxRetryCount)
        {
            logger.LogWarning("Workflow '{RunId}' exceeded max retry count ({MaxRetries}), aborting",
                this.RunId, MaxRetryCount);

            await this.AbortAsync($"Exceeded maximum retry count ({MaxRetryCount})", cancellationToken);
            return;
        }

        // Calculate if enough time has passed for a retry (exponential backoff)
        var backoffDelay = TimeSpan.FromTicks(Math.Min(
            BaseRetryDelay.Ticks * (1L << retryCount),
            MaxRetryDelay.Ticks));

        var timeSinceLastAttempt = lastAttempt.HasValue
            ? DateTimeOffset.UtcNow - lastAttempt.Value
            : TimeSpan.MaxValue;

        // Determine if we should retry based on execution state
        var shouldRetry = execState switch
        {
            // Not started - this is initial dispatch, should be handled by HTTP API
            WorkflowExecutionState.NotStarted when run.Status == WorkflowRunStatus.Queued
                && timeSinceLastAttempt > ExecutionTimeout => true,

            // Dispatched but no worker updates received - worker may have crashed
            WorkflowExecutionState.Dispatched when timeSinceLastAttempt > ExecutionTimeout => true,

            // Executing but no recent updates - worker may have crashed
            WorkflowExecutionState.Executing when timeSinceLastAttempt > ExecutionTimeout => true,

            // Resume was dispatched but no updates - worker may have crashed during resume
            WorkflowExecutionState.ResumeDispatched when timeSinceLastAttempt > ExecutionTimeout => true,

            // Resuming but no recent updates
            WorkflowExecutionState.Resuming when timeSinceLastAttempt > ExecutionTimeout => true,

            // Waiting for signal - nothing to retry, user action needed
            WorkflowExecutionState.WaitingForSignal => false,

            // Terminal states - nothing to retry
            WorkflowExecutionState.Completed or WorkflowExecutionState.Failed => false,

            _ => false
        };

        if (!shouldRetry || timeSinceLastAttempt < backoffDelay)
        {
            return;
        }

        logger.LogInformation(
            "Workflow '{RunId}' retrying execution (attempt {Attempt}/{MaxRetries}, state: {State}, last attempt: {LastAttempt})",
            this.RunId, retryCount + 1, MaxRetryCount, execState, lastAttempt);

        // Increment retry count and update state
        workflowState.State.RetryCount = retryCount + 1;
        workflowState.State.LastExecutionAttempt = DateTimeOffset.UtcNow;
        await workflowState.WriteStateAsync(cancellationToken);

        try
        {
            // Determine what type of retry to perform
            if (workflowState.State.PendingSignal is { } signal)
            {
                // We have a pending signal - retry the resume
                await this.RetryResumeAsync(signal, cancellationToken);
            }
            else if (run.Status is WorkflowRunStatus.Queued or WorkflowRunStatus.Running)
            {
                // No pending signal - retry initial execution
                await this.RetryExecuteAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workflow '{RunId}' retry failed", this.RunId);
            // Don't abort - let the reminder try again on next tick
        }
    }

    private async Task RetryExecuteAsync(CancellationToken cancellationToken)
    {
        var run = workflowState.State.Run!;

        // Build callback URL - use a default since we don't have HTTP context
        // The worker will call back to update state
        var callbackBaseUrl = GetCallbackBaseUrl();

        var executionRequest = new WorkflowExecutionRequest
        {
            RunId = this.RunId,
            WorkflowName = run.WorkflowName,
            Input = run.Input!,
            CallbackBaseUrl = callbackBaseUrl,
            Options = run.Metadata
        };

        workflowState.State.ExecutionState = WorkflowExecutionState.Dispatched;
        await workflowState.WriteStateAsync(cancellationToken);

        logger.LogInformation("Workflow '{RunId}' dispatching retry execution to worker", this.RunId);

        var result = await workflowExecutor.ExecuteAsync(
            executionRequest,
            workflowState.State.AssignedWorkerId,
            cancellationToken);

        if (!result.Success)
        {
            logger.LogError(
                "Workflow '{RunId}' retry dispatch failed: {ErrorCode} - {ErrorMessage}",
                this.RunId, result.ErrorCode, result.ErrorMessage);
        }
        else if (!string.IsNullOrEmpty(result.WorkerId))
        {
            workflowState.State.AssignedWorkerId = result.WorkerId;
            await workflowState.WriteStateAsync(cancellationToken);
        }
    }

    private async Task RetryResumeAsync(WorkflowSignal signal, CancellationToken cancellationToken)
    {
        var run = workflowState.State.Run!;

        var callbackBaseUrl = GetCallbackBaseUrl();

        var resumeRequest = new WorkflowResumeRequest
        {
            RunId = this.RunId,
            WorkflowName = run.WorkflowName,
            CallbackBaseUrl = callbackBaseUrl,
            Signal = signal,
            CheckpointData = workflowState.State.Checkpoint?.Data
        };

        workflowState.State.ExecutionState = WorkflowExecutionState.ResumeDispatched;
        await workflowState.WriteStateAsync(cancellationToken);

        logger.LogInformation("Workflow '{RunId}' dispatching retry resume to worker", this.RunId);

        var result = await workflowExecutor.ResumeAsync(
            resumeRequest,
            workflowState.State.AssignedWorkerId,
            cancellationToken);

        if (!result.Success)
        {
            logger.LogError(
                "Workflow '{RunId}' retry resume dispatch failed: {ErrorCode} - {ErrorMessage}",
                this.RunId, result.ErrorCode, result.ErrorMessage);
        }
        else if (!string.IsNullOrEmpty(result.WorkerId))
        {
            workflowState.State.AssignedWorkerId = result.WorkerId;
            await workflowState.WriteStateAsync(cancellationToken);
        }
    }

    private static string GetCallbackBaseUrl()
    {
        // In a production system, this should be configured via options
        // For now, we return a placeholder that the gateway should resolve
        return Environment.GetEnvironmentVariable("GATEWAY_CALLBACK_URL") ?? "http://localhost:5000";
    }

    // ============ Helpers ============

    private void EnsureRunExists()
    {
        if (workflowState.State.Run is null)
        {
            throw WorkflowNotFoundException.ForRunId(this.RunId);
        }
    }

    private WorkflowRun GetRunWithETag()
    {
        Debug.Assert(workflowState.State.Run is not null);
        return workflowState.State.Run with { ETag = workflowState.State.GetETag() };
    }

    private static bool IsTerminalStatus(WorkflowRunStatus status)
    {
        return status is WorkflowRunStatus.Completed
            or WorkflowRunStatus.Cancelled
            or WorkflowRunStatus.Aborted
            or WorkflowRunStatus.Failed;
    }

    private async Task UnregisterReminderIfExistsAsync()
    {
        try
        {
            var reminder = await this.GetReminder(ExecutionReminderName);
            if (reminder is not null)
            {
                await this.UnregisterReminder(reminder);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to unregister reminder for workflow '{RunId}'", this.RunId);
        }
    }

    public void Dispose()
    {
        this._shutdownCts.Dispose();
    }
}
