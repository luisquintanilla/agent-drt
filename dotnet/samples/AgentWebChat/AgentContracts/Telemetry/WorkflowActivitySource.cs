// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics;

namespace AgentContracts.Telemetry;

/// <summary>
/// Central ActivitySource for workflow tracing operations.
/// Provides factory methods for creating standardized spans.
/// </summary>
public static class WorkflowActivitySource
{
    /// <summary>
    /// The ActivitySource for workflow operations.
    /// </summary>
    public static readonly ActivitySource Source = new(TelemetryConstants.WorkflowActivitySourceName, "1.0.0");

    /// <summary>
    /// The ActivitySource for monitoring operations.
    /// </summary>
    public static readonly ActivitySource MonitoringSource = new(TelemetryConstants.MonitoringActivitySourceName, "1.0.0");

    // ============ Workflow Lifecycle Spans ============

    /// <summary>
    /// Starts an activity for workflow execution.
    /// </summary>
    public static Activity? StartWorkflowExecution(string runId, string workflowName, ActivityKind kind = ActivityKind.Internal)
    {
        var activity = Source.StartActivity($"workflow.execute {workflowName}", kind);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.WorkflowName, workflowName);
        return activity;
    }

    /// <summary>
    /// Starts an activity for workflow resume.
    /// </summary>
    public static Activity? StartWorkflowResume(string runId, string workflowName, string signalRequestId)
    {
        var activity = Source.StartActivity($"workflow.resume {workflowName}", ActivityKind.Internal);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.WorkflowName, workflowName);
        activity?.SetTag(TelemetryConstants.SignalRequestId, signalRequestId);
        return activity;
    }

    /// <summary>
    /// Starts an activity for workflow status update.
    /// </summary>
    public static Activity? StartStatusUpdate(string runId, string fromStatus, string toStatus)
    {
        var activity = Source.StartActivity("workflow.status_update", ActivityKind.Internal);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.WorkflowPreviousStatus, fromStatus);
        activity?.SetTag(TelemetryConstants.WorkflowStatus, toStatus);
        return activity;
    }

    // ============ Step Spans ============

    /// <summary>
    /// Starts an activity for step execution.
    /// </summary>
    public static Activity? StartStepExecution(string runId, string stepId, string executorId, string? executorName = null)
    {
        var activity = Source.StartActivity($"workflow.step {executorName ?? executorId}", ActivityKind.Internal);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.StepId, stepId);
        activity?.SetTag(TelemetryConstants.StepExecutorId, executorId);
        if (executorName != null)
        {
            activity?.SetTag(TelemetryConstants.StepExecutorName, executorName);
        }
        return activity;
    }

    /// <summary>
    /// Records step completion on an activity.
    /// </summary>
    public static void RecordStepCompletion(Activity? activity, long durationMs, bool success)
    {
        activity?.SetTag(TelemetryConstants.StepDurationMs, durationMs);
        if (!success)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
    }

    // ============ Signal Spans ============

    /// <summary>
    /// Starts an activity for signal request (waiting for external input).
    /// </summary>
    public static Activity? StartSignalRequest(string runId, string requestId, string portId)
    {
        var activity = Source.StartActivity("workflow.signal.request", ActivityKind.Internal);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.SignalRequestId, requestId);
        activity?.SetTag(TelemetryConstants.SignalPortId, portId);
        return activity;
    }

    /// <summary>
    /// Starts an activity for signal delivery.
    /// </summary>
    public static Activity? StartSignalDelivery(string runId, string requestId)
    {
        var activity = Source.StartActivity("workflow.signal.deliver", ActivityKind.Internal);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.SignalRequestId, requestId);
        return activity;
    }

    // ============ Checkpoint Spans ============

    /// <summary>
    /// Starts an activity for saving a checkpoint.
    /// </summary>
    public static Activity? StartSaveCheckpoint(string runId, string checkpointId)
    {
        var activity = Source.StartActivity("workflow.checkpoint.save", ActivityKind.Internal);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.CheckpointId, checkpointId);
        return activity;
    }

    /// <summary>
    /// Starts an activity for loading a checkpoint.
    /// </summary>
    public static Activity? StartLoadCheckpoint(string runId)
    {
        var activity = Source.StartActivity("workflow.checkpoint.load", ActivityKind.Internal);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        return activity;
    }

    // ============ Worker Dispatch Spans ============

    /// <summary>
    /// Starts an activity for dispatching work to a worker.
    /// </summary>
    public static Activity? StartWorkerDispatch(string runId, string workflowName, string workerId, string workerAddress)
    {
        var activity = Source.StartActivity($"worker.dispatch {workflowName}", ActivityKind.Client);
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.WorkflowName, workflowName);
        activity?.SetTag(TelemetryConstants.WorkerId, workerId);
        activity?.SetTag(TelemetryConstants.WorkerAddress, workerAddress);
        return activity;
    }

    /// <summary>
    /// Starts an activity for worker health check.
    /// </summary>
    public static Activity? StartWorkerHealthCheck(string workerId, string workerAddress)
    {
        var activity = Source.StartActivity("worker.health_check", ActivityKind.Client);
        activity?.SetTag(TelemetryConstants.WorkerId, workerId);
        activity?.SetTag(TelemetryConstants.WorkerAddress, workerAddress);
        return activity;
    }

    // ============ HTTP API Spans ============

    /// <summary>
    /// Starts an activity for an HTTP API operation.
    /// </summary>
    public static Activity? StartHttpOperation(string operationName, string method, string path)
    {
        var activity = Source.StartActivity($"http.{operationName}", ActivityKind.Server);
        activity?.SetTag(TelemetryConstants.HttpMethod, method);
        activity?.SetTag(TelemetryConstants.HttpPath, path);
        return activity;
    }

    /// <summary>
    /// Records HTTP response on an activity.
    /// </summary>
    public static void RecordHttpResponse(Activity? activity, int statusCode)
    {
        activity?.SetTag(TelemetryConstants.HttpStatusCode, statusCode);
        if (statusCode >= 400)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
    }

    // ============ Grain Spans ============

    /// <summary>
    /// Starts an activity for a grain operation.
    /// </summary>
    public static Activity? StartGrainOperation(string operationName, string grainType, string grainKey)
    {
        var activity = Source.StartActivity($"grain.{operationName}", ActivityKind.Internal);
        activity?.SetTag(TelemetryConstants.OrleansGrainType, grainType);
        activity?.SetTag(TelemetryConstants.OrleansGrainKey, grainKey);
        return activity;
    }

    // ============ Monitoring Spans ============

    /// <summary>
    /// Starts an activity for monitoring data retrieval.
    /// </summary>
    public static Activity? StartMonitoringOperation(string operationName)
    {
        return MonitoringSource.StartActivity($"monitoring.{operationName}", ActivityKind.Internal);
    }

    // ============ Utility Methods ============

    /// <summary>
    /// Records an exception on an activity.
    /// </summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        if (activity == null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        }));
    }

    /// <summary>
    /// Adds a custom event to an activity.
    /// </summary>
    public static void AddEvent(Activity? activity, string eventName, params (string Key, object? Value)[] tags)
    {
        if (activity == null) return;

        var tagCollection = new ActivityTagsCollection();
        foreach (var (key, value) in tags)
        {
            if (value != null)
            {
                tagCollection.Add(key, value);
            }
        }
        activity.AddEvent(new ActivityEvent(eventName, tags: tagCollection));
    }
}
