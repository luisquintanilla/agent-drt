// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentContracts.Monitoring;

/// <summary>
/// Health state of a worker.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkerHealthState>))]
public enum WorkerHealthState
{
    /// <summary>Worker health is unknown (not yet checked).</summary>
    Unknown,

    /// <summary>Worker is healthy and accepting work.</summary>
    Healthy,

    /// <summary>Worker is unhealthy (failed health checks).</summary>
    Unhealthy,

    /// <summary>Worker is draining (not accepting new work).</summary>
    Draining,

    /// <summary>Worker is offline.</summary>
    Offline
}

/// <summary>
/// System-wide status overview.
/// </summary>
public sealed record SystemStatus
{
    /// <summary>Timestamp of this status snapshot.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Number of currently active (running) workflows.</summary>
    [JsonPropertyName("activeWorkflows")]
    public int ActiveWorkflows { get; init; }

    /// <summary>Number of queued workflows.</summary>
    [JsonPropertyName("queuedWorkflows")]
    public int QueuedWorkflows { get; init; }

    /// <summary>Number of workflows waiting for signals.</summary>
    [JsonPropertyName("waitingForSignalWorkflows")]
    public int WaitingForSignalWorkflows { get; init; }

    /// <summary>Number of workflows completed in the last 24 hours.</summary>
    [JsonPropertyName("completedWorkflows24h")]
    public int CompletedWorkflows24h { get; init; }

    /// <summary>Number of workflows failed in the last 24 hours.</summary>
    [JsonPropertyName("failedWorkflows24h")]
    public int FailedWorkflows24h { get; init; }

    /// <summary>Total number of registered workers.</summary>
    [JsonPropertyName("registeredWorkers")]
    public int RegisteredWorkers { get; init; }

    /// <summary>Number of healthy workers.</summary>
    [JsonPropertyName("healthyWorkers")]
    public int HealthyWorkers { get; init; }

    /// <summary>Number of drained workers.</summary>
    [JsonPropertyName("drainedWorkers")]
    public int DrainedWorkers { get; init; }

    /// <summary>Gateway uptime.</summary>
    [JsonPropertyName("uptime")]
    public TimeSpan Uptime { get; init; }

    /// <summary>Gateway version.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

/// <summary>
/// Detailed status of a worker.
/// </summary>
public sealed record WorkerStatus
{
    /// <summary>Unique worker identifier.</summary>
    [JsonPropertyName("workerId")]
    public required string WorkerId { get; init; }

    /// <summary>Worker address (URL).</summary>
    [JsonPropertyName("address")]
    public required string Address { get; init; }

    /// <summary>Current health state.</summary>
    [JsonPropertyName("health")]
    public WorkerHealthState Health { get; init; }

    /// <summary>Timestamp of last health check.</summary>
    [JsonPropertyName("lastHealthCheck")]
    public DateTimeOffset? LastHealthCheck { get; init; }

    /// <summary>When the worker registered.</summary>
    [JsonPropertyName("registeredAt")]
    public DateTimeOffset RegisteredAt { get; init; }

    /// <summary>Number of active workflows on this worker.</summary>
    [JsonPropertyName("activeWorkflows")]
    public int ActiveWorkflows { get; init; }

    /// <summary>List of workflow names this worker supports.</summary>
    [JsonPropertyName("supportedWorkflows")]
    public IReadOnlyList<string> SupportedWorkflows { get; init; } = [];

    /// <summary>List of agent names this worker supports.</summary>
    [JsonPropertyName("supportedAgents")]
    public IReadOnlyList<string> SupportedAgents { get; init; } = [];

    /// <summary>Worker metrics.</summary>
    [JsonPropertyName("metrics")]
    public WorkerMetricsSnapshot? Metrics { get; init; }

    /// <summary>Whether the worker is draining.</summary>
    [JsonPropertyName("isDraining")]
    public bool IsDraining { get; init; }
}

/// <summary>
/// Worker performance metrics.
/// </summary>
public sealed record WorkerMetricsSnapshot
{
    /// <summary>Total workflows executed by this worker.</summary>
    [JsonPropertyName("totalWorkflowsExecuted")]
    public long TotalWorkflowsExecuted { get; init; }

    /// <summary>Total workflows failed on this worker.</summary>
    [JsonPropertyName("totalWorkflowsFailed")]
    public long TotalWorkflowsFailed { get; init; }

    /// <summary>Average workflow execution time in milliseconds.</summary>
    [JsonPropertyName("avgExecutionTimeMs")]
    public double AvgExecutionTimeMs { get; init; }

    /// <summary>Last recorded CPU usage percentage (if available).</summary>
    [JsonPropertyName("cpuUsagePercent")]
    public double? CpuUsagePercent { get; init; }

    /// <summary>Last recorded memory usage in MB (if available).</summary>
    [JsonPropertyName("memoryUsageMB")]
    public long? MemoryUsageMB { get; init; }
}

/// <summary>
/// Summary of a workflow run for monitoring purposes.
/// </summary>
public sealed record WorkflowMonitoringSummary
{
    /// <summary>Workflow run ID.</summary>
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    /// <summary>Workflow definition name.</summary>
    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    /// <summary>Current workflow status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>When the workflow was created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the workflow started executing.</summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the workflow completed (if completed).</summary>
    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Total duration of the workflow.</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }

    /// <summary>ID of the worker assigned to this workflow.</summary>
    [JsonPropertyName("assignedWorker")]
    public string? AssignedWorker { get; init; }

    /// <summary>Total number of steps in the workflow.</summary>
    [JsonPropertyName("stepCount")]
    public int StepCount { get; init; }

    /// <summary>Number of completed steps.</summary>
    [JsonPropertyName("completedSteps")]
    public int CompletedSteps { get; init; }

    /// <summary>Name of the current step (if running).</summary>
    [JsonPropertyName("currentStep")]
    public string? CurrentStep { get; init; }

    /// <summary>Whether the workflow has pending signal requests.</summary>
    [JsonPropertyName("hasPendingSignal")]
    public bool HasPendingSignal { get; init; }

    /// <summary>Error message if the workflow failed.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Aggregated workflow metrics for a time window.
/// </summary>
public sealed record WorkflowMetricsSnapshot
{
    /// <summary>Start of the metrics window.</summary>
    [JsonPropertyName("windowStart")]
    public DateTimeOffset WindowStart { get; init; }

    /// <summary>End of the metrics window.</summary>
    [JsonPropertyName("windowEnd")]
    public DateTimeOffset WindowEnd { get; init; }

    /// <summary>Total workflows started in the window.</summary>
    [JsonPropertyName("workflowsStarted")]
    public int WorkflowsStarted { get; init; }

    /// <summary>Total workflows completed in the window.</summary>
    [JsonPropertyName("workflowsCompleted")]
    public int WorkflowsCompleted { get; init; }

    /// <summary>Total workflows failed in the window.</summary>
    [JsonPropertyName("workflowsFailed")]
    public int WorkflowsFailed { get; init; }

    /// <summary>Average workflow duration in milliseconds.</summary>
    [JsonPropertyName("avgDurationMs")]
    public double AvgDurationMs { get; init; }

    /// <summary>P50 workflow duration in milliseconds.</summary>
    [JsonPropertyName("p50DurationMs")]
    public double P50DurationMs { get; init; }

    /// <summary>P95 workflow duration in milliseconds.</summary>
    [JsonPropertyName("p95DurationMs")]
    public double P95DurationMs { get; init; }

    /// <summary>P99 workflow duration in milliseconds.</summary>
    [JsonPropertyName("p99DurationMs")]
    public double P99DurationMs { get; init; }

    /// <summary>Total steps executed in the window.</summary>
    [JsonPropertyName("stepsExecuted")]
    public int StepsExecuted { get; init; }

    /// <summary>Average step duration in milliseconds.</summary>
    [JsonPropertyName("avgStepDurationMs")]
    public double AvgStepDurationMs { get; init; }

    /// <summary>Total signals processed in the window.</summary>
    [JsonPropertyName("signalsProcessed")]
    public int SignalsProcessed { get; init; }

    /// <summary>Breakdown by workflow name.</summary>
    [JsonPropertyName("byWorkflowName")]
    public IReadOnlyDictionary<string, WorkflowNameMetrics>? ByWorkflowName { get; init; }
}

/// <summary>
/// Metrics for a specific workflow name.
/// </summary>
public sealed record WorkflowNameMetrics
{
    /// <summary>Number of runs started.</summary>
    [JsonPropertyName("started")]
    public int Started { get; init; }

    /// <summary>Number of runs completed.</summary>
    [JsonPropertyName("completed")]
    public int Completed { get; init; }

    /// <summary>Number of runs failed.</summary>
    [JsonPropertyName("failed")]
    public int Failed { get; init; }

    /// <summary>Average duration in milliseconds.</summary>
    [JsonPropertyName("avgDurationMs")]
    public double AvgDurationMs { get; init; }
}

/// <summary>
/// Real-time monitoring event for SSE streaming.
/// </summary>
public sealed record MonitoringEvent
{
    /// <summary>Event type (e.g., "workflow.started", "worker.registered").</summary>
    [JsonPropertyName("eventType")]
    public required string EventType { get; init; }

    /// <summary>Event timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Event payload data.</summary>
    [JsonPropertyName("payload")]
    public required object Payload { get; init; }

    /// <summary>Optional correlation ID for related events.</summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Known monitoring event types.
/// </summary>
public static class MonitoringEventTypes
{
    // Workflow events
    public const string WorkflowStarted = "workflow.started";
    public const string WorkflowCompleted = "workflow.completed";
    public const string WorkflowFailed = "workflow.failed";
    public const string WorkflowCancelled = "workflow.cancelled";
    public const string WorkflowAborted = "workflow.aborted";
    public const string WorkflowWaitingForSignal = "workflow.waiting_for_signal";
    public const string WorkflowSignalReceived = "workflow.signal_received";
    public const string WorkflowStepStarted = "workflow.step.started";
    public const string WorkflowStepCompleted = "workflow.step.completed";

    // Worker events
    public const string WorkerRegistered = "worker.registered";
    public const string WorkerDeregistered = "worker.deregistered";
    public const string WorkerHealthChanged = "worker.health_changed";
    public const string WorkerDrained = "worker.drained";
    public const string WorkerEnabled = "worker.enabled";

    // System events
    public const string SystemStatusChanged = "system.status_changed";
    public const string SystemError = "system.error";
}

/// <summary>
/// Payload for workflow monitoring events.
/// </summary>
public sealed record WorkflowEventPayload
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("previousStatus")]
    public string? PreviousStatus { get; init; }

    [JsonPropertyName("stepName")]
    public string? StepName { get; init; }

    [JsonPropertyName("workerId")]
    public string? WorkerId { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }
}

/// <summary>
/// Payload for worker monitoring events.
/// </summary>
public sealed record WorkerEventPayload
{
    [JsonPropertyName("workerId")]
    public required string WorkerId { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("health")]
    public string? Health { get; init; }

    [JsonPropertyName("previousHealth")]
    public string? PreviousHealth { get; init; }

    [JsonPropertyName("activeWorkflows")]
    public int? ActiveWorkflows { get; init; }
}
