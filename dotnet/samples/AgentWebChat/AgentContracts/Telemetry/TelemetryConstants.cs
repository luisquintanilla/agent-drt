// Copyright (c) Microsoft. All rights reserved.

namespace AgentContracts.Telemetry;

/// <summary>
/// Standardized telemetry tag/attribute names for workflow tracing.
/// Follows OpenTelemetry semantic conventions where applicable.
/// </summary>
public static class TelemetryConstants
{
    /// <summary>
    /// Activity source name for workflow operations.
    /// </summary>
    public const string WorkflowActivitySourceName = "AgentWebChat.Workflows";

    /// <summary>
    /// Activity source name for monitoring operations.
    /// </summary>
    public const string MonitoringActivitySourceName = "AgentWebChat.Monitoring";

    /// <summary>
    /// Meter name for workflow metrics.
    /// </summary>
    public const string WorkflowMeterName = "AgentWebChat.Workflows";

    /// <summary>
    /// Meter name for worker metrics.
    /// </summary>
    public const string WorkerMeterName = "AgentWebChat.Workers";

    // ============ Workflow Attributes ============

    /// <summary>Unique identifier for the workflow run.</summary>
    public const string WorkflowRunId = "workflow.run_id";

    /// <summary>Name of the workflow definition.</summary>
    public const string WorkflowName = "workflow.name";

    /// <summary>Current status of the workflow.</summary>
    public const string WorkflowStatus = "workflow.status";

    /// <summary>Previous status before a transition.</summary>
    public const string WorkflowPreviousStatus = "workflow.previous_status";

    /// <summary>Size of workflow input in bytes.</summary>
    public const string WorkflowInputSize = "workflow.input.size_bytes";

    /// <summary>Size of workflow output in bytes.</summary>
    public const string WorkflowOutputSize = "workflow.output.size_bytes";

    /// <summary>Duration of workflow execution in milliseconds.</summary>
    public const string WorkflowDurationMs = "workflow.duration_ms";

    /// <summary>Error code if workflow failed.</summary>
    public const string WorkflowErrorCode = "workflow.error.code";

    /// <summary>Error message if workflow failed.</summary>
    public const string WorkflowErrorMessage = "workflow.error.message";

    // ============ Workflow Step Attributes ============

    /// <summary>Unique identifier for the step.</summary>
    public const string StepId = "workflow.step.id";

    /// <summary>Display name of the step.</summary>
    public const string StepName = "workflow.step.name";

    /// <summary>Index position of the step in the workflow.</summary>
    public const string StepIndex = "workflow.step.index";

    /// <summary>ID of the executor (agent) running the step.</summary>
    public const string StepExecutorId = "workflow.step.executor_id";

    /// <summary>Name of the executor (agent) running the step.</summary>
    public const string StepExecutorName = "workflow.step.executor_name";

    /// <summary>Duration of step execution in milliseconds.</summary>
    public const string StepDurationMs = "workflow.step.duration_ms";

    // ============ Signal Attributes ============

    /// <summary>Type of signal (e.g., "human_approval", "data_input").</summary>
    public const string SignalType = "workflow.signal.type";

    /// <summary>Request ID for the pending signal.</summary>
    public const string SignalRequestId = "workflow.signal.request_id";

    /// <summary>Port ID for the signal.</summary>
    public const string SignalPortId = "workflow.signal.port_id";

    // ============ Worker Attributes ============

    /// <summary>Unique identifier for the worker.</summary>
    public const string WorkerId = "worker.id";

    /// <summary>Address (URL) of the worker.</summary>
    public const string WorkerAddress = "worker.address";

    /// <summary>Health state of the worker.</summary>
    public const string WorkerHealthState = "worker.health_state";

    /// <summary>Number of active workflows on the worker.</summary>
    public const string WorkerActiveWorkflows = "worker.active_workflows";

    // ============ Checkpoint Attributes ============

    /// <summary>Checkpoint ID.</summary>
    public const string CheckpointId = "workflow.checkpoint.id";

    /// <summary>Size of checkpoint data in bytes.</summary>
    public const string CheckpointSize = "workflow.checkpoint.size_bytes";

    // ============ Artifact Attributes ============

    /// <summary>Artifact ID.</summary>
    public const string ArtifactId = "workflow.artifact.id";

    /// <summary>Artifact name.</summary>
    public const string ArtifactName = "workflow.artifact.name";

    /// <summary>Artifact content type.</summary>
    public const string ArtifactContentType = "workflow.artifact.content_type";

    /// <summary>Artifact size in bytes.</summary>
    public const string ArtifactSize = "workflow.artifact.size_bytes";

    // ============ HTTP Attributes ============

    /// <summary>HTTP request method.</summary>
    public const string HttpMethod = "http.method";

    /// <summary>HTTP response status code.</summary>
    public const string HttpStatusCode = "http.status_code";

    /// <summary>HTTP request path.</summary>
    public const string HttpPath = "http.path";

    // ============ Orleans Attributes ============

    /// <summary>Orleans grain type.</summary>
    public const string OrleansGrainType = "orleans.grain.type";

    /// <summary>Orleans grain key.</summary>
    public const string OrleansGrainKey = "orleans.grain.key";

    // ============ Event Names ============

    /// <summary>Event name for workflow state changes.</summary>
    public const string EventWorkflowStateChanged = "workflow.state_changed";

    /// <summary>Event name for step completion.</summary>
    public const string EventStepCompleted = "workflow.step.completed";

    /// <summary>Event name for signal received.</summary>
    public const string EventSignalReceived = "workflow.signal.received";

    /// <summary>Event name for checkpoint saved.</summary>
    public const string EventCheckpointSaved = "workflow.checkpoint.saved";

    /// <summary>Event name for artifact created.</summary>
    public const string EventArtifactCreated = "workflow.artifact.created";

    /// <summary>Event name for worker dispatch.</summary>
    public const string EventWorkerDispatched = "worker.dispatched";

    /// <summary>Event name for worker health check.</summary>
    public const string EventWorkerHealthCheck = "worker.health_check";
}
