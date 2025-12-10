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
        this._workflowMeter = new Meter(TelemetryConstants.WorkflowMeterName, "1.0.0");
        this._workerMeter = new Meter(TelemetryConstants.WorkerMeterName, "1.0.0");

        // Workflow Counters
        this.WorkflowRunsTotal = this._workflowMeter.CreateCounter<long>(
            "workflow.runs.total",
            unit: "{runs}",
            description: "Total number of workflow runs started");

        this.WorkflowRunsCompleted = this._workflowMeter.CreateCounter<long>(
            "workflow.runs.completed",
            unit: "{runs}",
            description: "Total number of workflow runs completed successfully");

        this.WorkflowRunsFailed = this._workflowMeter.CreateCounter<long>(
            "workflow.runs.failed",
            unit: "{runs}",
            description: "Total number of workflow runs that failed");

        this.WorkflowRunsCancelled = this._workflowMeter.CreateCounter<long>(
            "workflow.runs.cancelled",
            unit: "{runs}",
            description: "Total number of workflow runs cancelled");

        this.WorkflowRunsAborted = this._workflowMeter.CreateCounter<long>(
            "workflow.runs.aborted",
            unit: "{runs}",
            description: "Total number of workflow runs aborted");

        this.WorkflowStepsTotal = this._workflowMeter.CreateCounter<long>(
            "workflow.steps.total",
            unit: "{steps}",
            description: "Total number of workflow steps executed");

        this.SignalsProcessed = this._workflowMeter.CreateCounter<long>(
            "workflow.signals.processed",
            unit: "{signals}",
            description: "Total number of signals processed");

        this.CheckpointsSaved = this._workflowMeter.CreateCounter<long>(
            "workflow.checkpoints.saved",
            unit: "{checkpoints}",
            description: "Total number of checkpoints saved");

        this.ArtifactsCreated = this._workflowMeter.CreateCounter<long>(
            "workflow.artifacts.created",
            unit: "{artifacts}",
            description: "Total number of artifacts created");

        // Workflow Gauges
        this.ActiveWorkflowRuns = this._workflowMeter.CreateUpDownCounter<long>(
            "workflow.runs.active",
            unit: "{runs}",
            description: "Number of currently active workflow runs");

        this.WorkflowsWaitingForSignal = this._workflowMeter.CreateUpDownCounter<long>(
            "workflow.runs.waiting_for_signal",
            unit: "{runs}",
            description: "Number of workflows waiting for external signals");

        this.PendingSignalRequests = this._workflowMeter.CreateUpDownCounter<long>(
            "workflow.signals.pending",
            unit: "{requests}",
            description: "Number of pending signal requests");

        // Workflow Histograms
        this.WorkflowRunDuration = this._workflowMeter.CreateHistogram<double>(
            "workflow.run.duration",
            unit: "ms",
            description: "Duration of workflow runs in milliseconds");

        this.WorkflowStepDuration = this._workflowMeter.CreateHistogram<double>(
            "workflow.step.duration",
            unit: "ms",
            description: "Duration of workflow steps in milliseconds");

        this.SignalWaitTime = this._workflowMeter.CreateHistogram<double>(
            "workflow.signal.wait_time",
            unit: "ms",
            description: "Time spent waiting for signals in milliseconds");

        this.CheckpointSize = this._workflowMeter.CreateHistogram<long>(
            "workflow.checkpoint.size",
            unit: "By",
            description: "Size of workflow checkpoints in bytes");

        // Worker Counters
        this.WorkerDispatchesTotal = this._workerMeter.CreateCounter<long>(
            "worker.dispatches.total",
            unit: "{dispatches}",
            description: "Total number of workflow dispatches to workers");

        this.WorkerDispatchesFailed = this._workerMeter.CreateCounter<long>(
            "worker.dispatches.failed",
            unit: "{dispatches}",
            description: "Total number of failed workflow dispatches");

        this.WorkerHealthChecksTotal = this._workerMeter.CreateCounter<long>(
            "worker.health_checks.total",
            unit: "{checks}",
            description: "Total number of worker health checks");

        this.WorkerHealthChecksFailed = this._workerMeter.CreateCounter<long>(
            "worker.health_checks.failed",
            unit: "{checks}",
            description: "Total number of failed worker health checks");

        this.WorkerRegistrations = this._workerMeter.CreateCounter<long>(
            "worker.registrations.total",
            unit: "{registrations}",
            description: "Total number of worker registrations");

        this.WorkerDeregistrations = this._workerMeter.CreateCounter<long>(
            "worker.deregistrations.total",
            unit: "{deregistrations}",
            description: "Total number of worker deregistrations");

        // Worker Gauges
        this.RegisteredWorkers = this._workerMeter.CreateUpDownCounter<long>(
            "worker.registered",
            unit: "{workers}",
            description: "Number of registered workers");

        this.HealthyWorkers = this._workerMeter.CreateUpDownCounter<long>(
            "worker.healthy",
            unit: "{workers}",
            description: "Number of healthy workers");

        this.DrainedWorkers = this._workerMeter.CreateUpDownCounter<long>(
            "worker.drained",
            unit: "{workers}",
            description: "Number of drained workers");

        // Worker Histograms
        this.WorkerDispatchLatency = this._workerMeter.CreateHistogram<double>(
            "worker.dispatch.latency",
            unit: "ms",
            description: "Latency of worker dispatches in milliseconds");

        this.WorkerHealthCheckLatency = this._workerMeter.CreateHistogram<double>(
            "worker.health_check.latency",
            unit: "ms",
            description: "Latency of worker health checks in milliseconds");
    }

    /// <summary>
    /// Records a workflow run started.
    /// </summary>
    public void RecordWorkflowStarted(string workflowName)
    {
        this.WorkflowRunsTotal.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        this.ActiveWorkflowRuns.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow run completed.
    /// </summary>
    public void RecordWorkflowCompleted(string workflowName, double durationMs)
    {
        this.WorkflowRunsCompleted.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        this.ActiveWorkflowRuns.Add(-1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        this.WorkflowRunDuration.Record(durationMs, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow run failed.
    /// </summary>
    public void RecordWorkflowFailed(string workflowName, string errorCode, double durationMs)
    {
        this.WorkflowRunsFailed.Add(1,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName),
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowErrorCode, errorCode));
        this.ActiveWorkflowRuns.Add(-1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        this.WorkflowRunDuration.Record(durationMs, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow entering waiting for signal state.
    /// </summary>
    public void RecordWorkflowWaitingForSignal(string workflowName)
    {
        this.WorkflowsWaitingForSignal.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow resuming from waiting for signal.
    /// </summary>
    public void RecordWorkflowResumedFromSignal(string workflowName, double waitTimeMs)
    {
        this.WorkflowsWaitingForSignal.Add(-1, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        this.SignalWaitTime.Record(waitTimeMs, new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
    }

    /// <summary>
    /// Records a workflow step execution.
    /// </summary>
    public void RecordStepExecution(string workflowName, string stepName, double durationMs)
    {
        this.WorkflowStepsTotal.Add(1,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName),
            new KeyValuePair<string, object?>(TelemetryConstants.StepName, stepName));
        this.WorkflowStepDuration.Record(durationMs,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName),
            new KeyValuePair<string, object?>(TelemetryConstants.StepName, stepName));
    }

    /// <summary>
    /// Records a worker dispatch.
    /// </summary>
    public void RecordWorkerDispatch(string workerId, string workflowName, bool success, double latencyMs)
    {
        this.WorkerDispatchesTotal.Add(1,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId),
            new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));

        if (!success)
        {
            this.WorkerDispatchesFailed.Add(1,
                new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId),
                new KeyValuePair<string, object?>(TelemetryConstants.WorkflowName, workflowName));
        }

        this.WorkerDispatchLatency.Record(latencyMs,
            new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
    }

    /// <summary>
    /// Records a worker registration.
    /// </summary>
    public void RecordWorkerRegistration(string workerId)
    {
        this.WorkerRegistrations.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
        this.RegisteredWorkers.Add(1);
    }

    /// <summary>
    /// Records a worker deregistration.
    /// </summary>
    public void RecordWorkerDeregistration(string workerId)
    {
        this.WorkerDeregistrations.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
        this.RegisteredWorkers.Add(-1);
    }

    /// <summary>
    /// Records a worker health check.
    /// </summary>
    public void RecordWorkerHealthCheck(string workerId, bool healthy, double latencyMs)
    {
        this.WorkerHealthChecksTotal.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));

        if (!healthy)
        {
            this.WorkerHealthChecksFailed.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
        }

        this.WorkerHealthCheckLatency.Record(latencyMs, new KeyValuePair<string, object?>(TelemetryConstants.WorkerId, workerId));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this._workflowMeter.Dispose();
        this._workerMeter.Dispose();
    }
}
