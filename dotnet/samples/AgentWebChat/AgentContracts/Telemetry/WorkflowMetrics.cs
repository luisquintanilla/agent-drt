// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace AgentContracts.Telemetry;

/// <summary>
/// Metrics definitions for workflow and worker monitoring.
/// Uses System.Diagnostics.Metrics for OpenTelemetry-compatible instrumentation.
/// </summary>
public sealed class WorkflowMetrics : IDisposable
{
    private readonly Meter _workflowMeter;
    private readonly Meter _workerMeter;

    // ============ Workflow Counters ============

    /// <summary>Counter for total workflow runs started.</summary>
    public Counter<long> WorkflowRunsTotal { get; }

    /// <summary>Counter for total workflow runs completed successfully.</summary>
    public Counter<long> WorkflowRunsCompleted { get; }

    /// <summary>Counter for total workflow runs failed.</summary>
    public Counter<long> WorkflowRunsFailed { get; }

    /// <summary>Counter for total workflow runs cancelled.</summary>
    public Counter<long> WorkflowRunsCancelled { get; }

    /// <summary>Counter for total workflow runs aborted.</summary>
    public Counter<long> WorkflowRunsAborted { get; }

    /// <summary>Counter for total workflow steps executed.</summary>
    public Counter<long> WorkflowStepsTotal { get; }

    /// <summary>Counter for total signals processed.</summary>
    public Counter<long> SignalsProcessed { get; }

    /// <summary>Counter for total checkpoints saved.</summary>
    public Counter<long> CheckpointsSaved { get; }

    /// <summary>Counter for total artifacts created.</summary>
    public Counter<long> ArtifactsCreated { get; }

    // ============ Workflow Gauges ============

    /// <summary>UpDownCounter for active workflow runs.</summary>
    public UpDownCounter<long> ActiveWorkflowRuns { get; }

    /// <summary>UpDownCounter for workflows waiting for signals.</summary>
    public UpDownCounter<long> WorkflowsWaitingForSignal { get; }

    /// <summary>UpDownCounter for pending signal requests.</summary>
    public UpDownCounter<long> PendingSignalRequests { get; }

    // ============ Workflow Histograms ============

    /// <summary>Histogram for workflow run duration in milliseconds.</summary>
    public Histogram<double> WorkflowRunDuration { get; }

    /// <summary>Histogram for workflow step duration in milliseconds.</summary>
    public Histogram<double> WorkflowStepDuration { get; }

    /// <summary>Histogram for signal wait time in milliseconds.</summary>
    public Histogram<double> SignalWaitTime { get; }

    /// <summary>Histogram for checkpoint size in bytes.</summary>
    public Histogram<long> CheckpointSize { get; }

    // ============ Worker Counters ============

    /// <summary>Counter for total worker dispatches.</summary>
    public Counter<long> WorkerDispatchesTotal { get; }

    /// <summary>Counter for failed worker dispatches.</summary>
    public Counter<long> WorkerDispatchesFailed { get; }

    /// <summary>Counter for worker health check attempts.</summary>
    public Counter<long> WorkerHealthChecksTotal { get; }

    /// <summary>Counter for failed worker health checks.</summary>
    public Counter<long> WorkerHealthChecksFailed { get; }

    /// <summary>Counter for worker registrations.</summary>
    public Counter<long> WorkerRegistrations { get; }

    /// <summary>Counter for worker deregistrations.</summary>
    public Counter<long> WorkerDeregistrations { get; }

    // ============ Worker Gauges ============

    /// <summary>UpDownCounter for registered workers.</summary>
    public UpDownCounter<long> RegisteredWorkers { get; }

    /// <summary>UpDownCounter for healthy workers.</summary>
    public UpDownCounter<long> HealthyWorkers { get; }

    /// <summary>UpDownCounter for drained workers.</summary>
    public UpDownCounter<long> DrainedWorkers { get; }

    // ============ Worker Histograms ============

    /// <summary>Histogram for worker dispatch latency in milliseconds.</summary>
    public Histogram<double> WorkerDispatchLatency { get; }

    /// <summary>Histogram for worker health check latency in milliseconds.</summary>
    public Histogram<double> WorkerHealthCheckLatency { get; }

    /// <summary>
    /// Creates a new instance of WorkflowMetrics.
    /// </summary>
    public WorkflowMetrics()
    {
        _workflowMeter = new Meter(TelemetryConstants.WorkflowMeterName, "1.0.0");
        _workerMeter = new Meter(TelemetryConstants.WorkerMeterName, "1.0.0");

        // Workflow Counters
        WorkflowRunsTotal = _workflowMeter.CreateCounter<long>(
            "workflow.runs.total",
            unit: "{runs}",
            description: "Total number of workflow runs started");

        WorkflowRunsCompleted = _workflowMeter.CreateCounter<long>(
            "workflow.runs.completed",
            unit: "{runs}",
            description: "Total number of workflow runs completed successfully");

        WorkflowRunsFailed = _workflowMeter.CreateCounter<long>(
            "workflow.runs.failed",
            unit: "{runs}",
            description: "Total number of workflow runs that failed");

        WorkflowRunsCancelled = _workflowMeter.CreateCounter<long>(
            "workflow.runs.cancelled",
            unit: "{runs}",
            description: "Total number of workflow runs cancelled");

        WorkflowRunsAborted = _workflowMeter.CreateCounter<long>(
            "workflow.runs.aborted",
            unit: "{runs}",
            description: "Total number of workflow runs aborted");

        WorkflowStepsTotal = _workflowMeter.CreateCounter<long>(
            "workflow.steps.total",
            unit: "{steps}",
            description: "Total number of workflow steps executed");

        SignalsProcessed = _workflowMeter.CreateCounter<long>(
            "workflow.signals.processed",
            unit: "{signals}",
            description: "Total number of signals processed");

        CheckpointsSaved = _workflowMeter.CreateCounter<long>(
            "workflow.checkpoints.saved",
            unit: "{checkpoints}",
            description: "Total number of checkpoints saved");

        ArtifactsCreated = _workflowMeter.CreateCounter<long>(
            "workflow.artifacts.created",
            unit: "{artifacts}",
            description: "Total number of artifacts created");

        // Workflow Gauges
        ActiveWorkflowRuns = _workflowMeter.CreateUpDownCounter<long>(
            "workflow.runs.active",
            unit: "{runs}",
            description: "Number of currently active workflow runs");

        WorkflowsWaitingForSignal = _workflowMeter.CreateUpDownCounter<long>(
            "workflow.runs.waiting_for_signal",
            unit: "{runs}",
            description: "Number of workflows waiting for external signals");

        PendingSignalRequests = _workflowMeter.CreateUpDownCounter<long>(
            "workflow.signals.pending",
            unit: "{requests}",
            description: "Number of pending signal requests");

        // Workflow Histograms
        WorkflowRunDuration = _workflowMeter.CreateHistogram<double>(
            "workflow.run.duration",
            unit: "ms",
            description: "Duration of workflow runs in milliseconds");

        WorkflowStepDuration = _workflowMeter.CreateHistogram<double>(
            "workflow.step.duration",
            unit: "ms",
            description: "Duration of workflow steps in milliseconds");

        SignalWaitTime = _workflowMeter.CreateHistogram<double>(
            "workflow.signal.wait_time",
            unit: "ms",
            description: "Time spent waiting for signals in milliseconds");

        CheckpointSize = _workflowMeter.CreateHistogram<long>(
            "workflow.checkpoint.size",
            unit: "By",
            description: "Size of workflow checkpoints in bytes");

        // Worker Counters
        WorkerDispatchesTotal = _workerMeter.CreateCounter<long>(
            "worker.dispatches.total",
            unit: "{dispatches}",
            description: "Total number of workflow dispatches to workers");

        WorkerDispatchesFailed = _workerMeter.CreateCounter<long>(
            "worker.dispatches.failed",
            unit: "{dispatches}",
            description: "Total number of failed workflow dispatches");

        WorkerHealthChecksTotal = _workerMeter.CreateCounter<long>(
            "worker.health_checks.total",
            unit: "{checks}",
            description: "Total number of worker health checks");

        WorkerHealthChecksFailed = _workerMeter.CreateCounter<long>(
            "worker.health_checks.failed",
            unit: "{checks}",
            description: "Total number of failed worker health checks");

        WorkerRegistrations = _workerMeter.CreateCounter<long>(
            "worker.registrations.total",
            unit: "{registrations}",
            description: "Total number of worker registrations");

        WorkerDeregistrations = _workerMeter.CreateCounter<long>(
            "worker.deregistrations.total",
            unit: "{deregistrations}",
            description: "Total number of worker deregistrations");

        // Worker Gauges
        RegisteredWorkers = _workerMeter.CreateUpDownCounter<long>(
            "worker.registered",
            unit: "{workers}",
            description: "Number of registered workers");

        HealthyWorkers = _workerMeter.CreateUpDownCounter<long>(
            "worker.healthy",
            unit: "{workers}",
            description: "Number of healthy workers");

        DrainedWorkers = _workerMeter.CreateUpDownCounter<long>(
            "worker.drained",
            unit: "{workers}",
            description: "Number of drained workers");

        // Worker Histograms
        WorkerDispatchLatency = _workerMeter.CreateHistogram<double>(
            "worker.dispatch.latency",
            unit: "ms",
            description: "Latency of worker dispatches in milliseconds");

        WorkerHealthCheckLatency = _workerMeter.CreateHistogram<double>(
            "worker.health_check.latency",
            unit: "ms",
            description: "Latency of worker health checks in milliseconds");
    }

    /// <summary>
    /// Records a workflow run started.
    /// </summary>
    public void RecordWorkflowStarted(string workflowName)
    {
        WorkflowRunsTotal.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        ActiveWorkflowRuns.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow run completed.
    /// </summary>
    public void RecordWorkflowCompleted(string workflowName, double durationMs)
    {
        WorkflowRunsCompleted.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        ActiveWorkflowRuns.Add(-1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        WorkflowRunDuration.Record(durationMs, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow run failed.
    /// </summary>
    public void RecordWorkflowFailed(string workflowName, string errorCode, double durationMs)
    {
        WorkflowRunsFailed.Add(1,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName),
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowErrorCode, errorCode));
        ActiveWorkflowRuns.Add(-1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        WorkflowRunDuration.Record(durationMs, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow entering waiting for signal state.
    /// </summary>
    public void RecordWorkflowWaitingForSignal(string workflowName)
    {
        WorkflowsWaitingForSignal.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow resuming from waiting for signal.
    /// </summary>
    public void RecordWorkflowResumedFromSignal(string workflowName, double waitTimeMs)
    {
        WorkflowsWaitingForSignal.Add(-1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        SignalWaitTime.Record(waitTimeMs, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow step execution.
    /// </summary>
    public void RecordStepExecution(string workflowName, string stepName, double durationMs)
    {
        WorkflowStepsTotal.Add(1,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName),
            new KeyValuePair<string, object?>(TelemetryConstants.StepName, stepName));
        WorkflowStepDuration.Record(durationMs,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName),
            new KeyValuePair<string, object?>(TelemetryConstants.StepName, stepName));
    }

    /// <summary>
    /// Records a worker dispatch.
    /// </summary>
    public void RecordWorkerDispatch(string workerId, string workflowName, bool success, double latencyMs)
    {
        WorkerDispatchesTotal.Add(1,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId),
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));

        if (!success)
        {
            WorkerDispatchesFailed.Add(1,
                new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId),
                new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        }

        WorkerDispatchLatency.Record(latencyMs,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
    }

    /// <summary>
    /// Records a worker registration.
    /// </summary>
    public void RecordWorkerRegistration(string workerId)
    {
        WorkerRegistrations.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
        RegisteredWorkers.Add(1);
    }

    /// <summary>
    /// Records a worker deregistration.
    /// </summary>
    public void RecordWorkerDeregistration(string workerId)
    {
        WorkerDeregistrations.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
        RegisteredWorkers.Add(-1);
    }

    /// <summary>
    /// Records a worker health check.
    /// </summary>
    public void RecordWorkerHealthCheck(string workerId, bool healthy, double latencyMs)
    {
        WorkerHealthChecksTotal.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));

        if (!healthy)
        {
            WorkerHealthChecksFailed.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
        }

        WorkerHealthCheckLatency.Record(latencyMs, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _workflowMeter.Dispose();
        _workerMeter.Dispose();
    }
}
