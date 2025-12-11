// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentContracts.Workflows;

/// <summary>
/// Base class for workflow status events (SSE).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WorkflowStartedEvent), "workflow.started")]
[JsonDerivedType(typeof(WorkflowStepStartedEvent), "step.started")]
[JsonDerivedType(typeof(WorkflowStepCompletedEvent), "step.completed")]
[JsonDerivedType(typeof(WorkflowSignalRequestedEvent), "signal.requested")]
[JsonDerivedType(typeof(WorkflowSignalReceivedEvent), "signal.received")]
[JsonDerivedType(typeof(WorkflowArtifactCreatedEvent), "artifact.created")]
[JsonDerivedType(typeof(WorkflowOutputDeltaEvent), "output.delta")]
[JsonDerivedType(typeof(WorkflowCompletedEvent), "workflow.completed")]
[JsonDerivedType(typeof(WorkflowCompletedSignalEvent), "workflow.completed.signal")]
[JsonDerivedType(typeof(WorkflowFailedEvent), "workflow.failed")]
[JsonDerivedType(typeof(WorkflowCancelledEvent), "workflow.cancelled")]
[JsonDerivedType(typeof(WorkflowAbortedEvent), "workflow.aborted")]
public abstract class WorkflowStatusEvent
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("sequenceNumber")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event emitted when a workflow starts.
/// </summary>
public sealed class WorkflowStartedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("input")]
    public WorkflowMessage? Input { get; init; }
}

/// <summary>
/// Event emitted when a workflow step starts execution.
/// </summary>
public sealed class WorkflowStepStartedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("step")]
    public required WorkflowStepStartedRecord Step { get; init; }
}

/// <summary>
/// Event emitted when a workflow step completes execution.
/// </summary>
public sealed class WorkflowStepCompletedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("step")]
    public required WorkflowStepCompletedRecord Step { get; init; }
}

/// <summary>
/// Event emitted when a workflow is waiting for an external signal at a RequestPort.
/// </summary>
public sealed class WorkflowSignalRequestedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("request")]
    public required PendingExternalRequest Request { get; init; }
}

/// <summary>
/// Event emitted when a workflow receives a signal (response to a pending request).
/// </summary>
public sealed class WorkflowSignalReceivedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("response")]
    public WorkflowMessage? Response { get; init; }
}

/// <summary>
/// Event emitted when a workflow produces an artifact.
/// </summary>
public sealed class WorkflowArtifactCreatedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("artifact")]
    public required WorkflowArtifactRecord Artifact { get; init; }
}

/// <summary>
/// Event emitted when a workflow completes successfully.
/// Contains the full workflow run state.
/// </summary>
public sealed class WorkflowCompletedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("workflow")]
    public required WorkflowRun Workflow { get; init; }
}

/// <summary>
/// Internal event used by AgentHost to signal workflow completion.
/// The Gateway will convert this to a full WorkflowCompletedEvent with the Workflow property.
/// </summary>
public sealed class WorkflowCompletedSignalEvent : WorkflowStatusEvent
{
}

/// <summary>
/// Event emitted when a workflow fails with an error.
/// </summary>
public sealed class WorkflowFailedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("error")]
    public required WorkflowErrorInfo Error { get; init; }
}

/// <summary>
/// Event emitted when a workflow is gracefully cancelled.
/// </summary>
public sealed class WorkflowCancelledEvent : WorkflowStatusEvent
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// Event emitted when a workflow is forcefully aborted.
/// </summary>
public sealed class WorkflowAbortedEvent : WorkflowStatusEvent
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("abortedBy")]
    public string? AbortedBy { get; init; }
}

/// <summary>
/// Event emitted when a workflow produces a streaming output delta (token-by-token content).
/// This enables real-time token streaming from AI models through the workflow system.
/// </summary>
public sealed class WorkflowOutputDeltaEvent : WorkflowStatusEvent
{
    /// <summary>
    /// The delta content (typically a token or small chunk of text).
    /// </summary>
    [JsonPropertyName("delta")]
    public required WorkflowOutputDelta Delta { get; init; }
}

/// <summary>
/// Represents a streaming output delta from a workflow step.
/// </summary>
public sealed class WorkflowOutputDelta
{
    /// <summary>
    /// The step ID that produced this delta.
    /// </summary>
    [JsonPropertyName("stepId")]
    public required string StepId { get; init; }

    /// <summary>
    /// The content delta (typically a token or small chunk of text).
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// The content type of the delta (e.g., "text/plain", "text/markdown").
    /// </summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = "text/plain";

    /// <summary>
    /// The index within the step's output (for ordering/reassembly).
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>
    /// Whether this is the final delta for this step.
    /// </summary>
    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; init; }

    /// <summary>
    /// Optional metadata about the delta (e.g., token probabilities, model info).
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}
