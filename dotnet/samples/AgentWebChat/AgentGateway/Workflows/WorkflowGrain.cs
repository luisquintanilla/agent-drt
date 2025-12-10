// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts.Workflows;
using AgentGateway.Utilities;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace AgentGateway.Workflows;

/// <summary>
/// Orleans grain implementation for managing a single workflow run.
/// Handles state persistence, SSE streaming, and Orleans Reminders for reliability.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class WorkflowGrain(
    [PersistentState("state")] IPersistentState<WorkflowGrainState> workflowState,
    IGrainFactory grainFactory,
    ILogger<WorkflowGrain> logger) : Grain, IWorkflowGrain, IRemindable, IDisposable
{
    private const string ExecutionReminderName = "WorkflowExecution";
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
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await this._shutdownCts.CancelAsync();
        this._stateUpdatedEvent.Cancel();
    }

    public async Task<WorkflowRun> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken)
    {
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

        // Register reminder for background execution
        await this.RegisterOrUpdateReminder(
            ExecutionReminderName,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

        this._stateUpdatedEvent.SignalAndReset();

        logger.LogInformation("Workflow '{RunId}' started with workflow name '{WorkflowName}'", this.RunId, request.WorkflowName);

        return this.GetRunWithETag();
    }

    public Task<WorkflowRun?> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(workflowState.State.Run is not null ? this.GetRunWithETag() : null);
    }

    public async Task<WorkflowRun> SendSignalAsync(WorkflowSignal signal, CancellationToken cancellationToken)
    {
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

        return this.GetRunWithETag();
    }

    public async Task<WorkflowRun> CancelAsync(CancellationToken cancellationToken)
    {
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
        this.EnsureRunExists();

        var now = DateTimeOffset.UtcNow;
        workflowState.State.Run = workflowState.State.Run! with
        {
            Status = update.Status,
            Error = update.Error,
            UpdatedAt = now,
            CompletedAt = IsTerminalStatus(update.Status) ? now : workflowState.State.Run.CompletedAt
        };

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
        this.EnsureRunExists();

        workflowState.State.Run = workflowState.State.Run! with
        {
            PendingRequests = [.. workflowState.State.Run.PendingRequests, request],
            Status = WorkflowRunStatus.WaitingForSignal,
            UpdatedAt = DateTimeOffset.UtcNow
        };

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

    // ============ IRemindable ============

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName == ExecutionReminderName)
        {
            logger.LogDebug("Workflow '{RunId}' reminder tick", this.RunId);

            // If workflow is in terminal state, unregister the reminder
            if (workflowState.State.Run is { } run && IsTerminalStatus(run.Status))
            {
                await this.UnregisterReminderIfExistsAsync();
            }
            // Otherwise the reminder ensures the grain stays active for potential resumption
        }
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
