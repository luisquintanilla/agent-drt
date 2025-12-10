// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentContracts.Workflows;

/// <summary>
/// Represents a message (input or output) in a workflow.
/// Provides type-safe JSON serialization with type metadata.
/// </summary>
public sealed class WorkflowMessage
{
    /// <summary>
    /// The fully-qualified type name of the data for deserialization.
    /// </summary>
    [JsonPropertyName("typeName")]
    public required string TypeName { get; init; }

    /// <summary>
    /// The data payload serialized as JSON.
    /// </summary>
    [JsonPropertyName("data")]
    public required JsonElement Data { get; init; }

    /// <summary>
    /// Optional metadata (correlation IDs, user info, etc.).
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Creates a WorkflowMessage from a typed object.
    /// </summary>
    public static WorkflowMessage Create<T>(T data, JsonSerializerOptions? options = null, Dictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var json = JsonSerializer.SerializeToElement(data, options);
        return new WorkflowMessage
        {
            TypeName = typeof(T).FullName ?? typeof(T).Name,
            Data = json,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Attempts to deserialize the data to the specified type.
    /// </summary>
    public T? As<T>(JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(Data.GetRawText(), options);
    }

    /// <summary>
    /// Checks if the TypeName matches the specified type.
    /// </summary>
    public bool Is<T>()
    {
        var targetName = typeof(T).FullName ?? typeof(T).Name;
        return string.Equals(TypeName, targetName, StringComparison.Ordinal);
    }
}

/// <summary>
/// A signal sent to a workflow waiting at a RequestPort.
/// </summary>
public sealed class WorkflowSignal
{
    /// <summary>
    /// The request ID this signal responds to.
    /// </summary>
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    /// <summary>
    /// The response data.
    /// </summary>
    [JsonPropertyName("response")]
    public required WorkflowMessage Response { get; init; }
}

/// <summary>
/// Status of a workflow run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowRunStatus>))]
public enum WorkflowRunStatus
{
    /// <summary>Workflow is queued but not yet started.</summary>
    Queued,

    /// <summary>Workflow is actively running.</summary>
    Running,

    /// <summary>Workflow is waiting for an external signal.</summary>
    WaitingForSignal,

    /// <summary>Cooperative cancellation in progress.</summary>
    Cancelling,

    /// <summary>Workflow completed successfully.</summary>
    Completed,

    /// <summary>Workflow was gracefully cancelled.</summary>
    Cancelled,

    /// <summary>Workflow was forcefully aborted.</summary>
    Aborted,

    /// <summary>Workflow failed with an error.</summary>
    Failed
}

/// <summary>
/// Full representation of a workflow run.
/// </summary>
public sealed record WorkflowRun
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("status")]
    public required WorkflowRunStatus Status { get; init; }

    [JsonPropertyName("input")]
    public WorkflowMessage? Input { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("steps")]
    public List<WorkflowStepInfo> Steps { get; init; } = [];

    [JsonPropertyName("artifacts")]
    public List<WorkflowArtifactRecord> Artifacts { get; init; } = [];

    [JsonPropertyName("pendingRequests")]
    public List<PendingExternalRequest> PendingRequests { get; init; } = [];

    [JsonPropertyName("error")]
    public WorkflowErrorInfo? Error { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// ETag for optimistic concurrency (opaque string, backed by version number).
    /// </summary>
    [JsonPropertyName("etag")]
    public string? ETag { get; init; }
}

/// <summary>
/// Summary of a workflow run for list operations.
/// </summary>
public sealed class WorkflowRunSummary
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("status")]
    public required WorkflowRunStatus Status { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("pendingRequestCount")]
    public int PendingRequestCount { get; init; }

    [JsonPropertyName("etag")]
    public string? ETag { get; init; }
}

/// <summary>
/// Information about a workflow step.
/// </summary>
public sealed record WorkflowStepInfo
{
    [JsonPropertyName("stepId")]
    public required string StepId { get; init; }

    [JsonPropertyName("executorId")]
    public required string ExecutorId { get; init; }

    [JsonPropertyName("executorName")]
    public string? ExecutorName { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("output")]
    public WorkflowMessage? Output { get; init; }

    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }

    [JsonIgnore]
    public bool IsCompleted => CompletedAt.HasValue;
}

/// <summary>
/// A pending external request the workflow is waiting on.
/// </summary>
public sealed class PendingExternalRequest
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("portId")]
    public required string PortId { get; init; }

    [JsonPropertyName("requestTypeName")]
    public required string RequestTypeName { get; init; }

    [JsonPropertyName("responseTypeName")]
    public required string ResponseTypeName { get; init; }

    /// <summary>
    /// The request data (e.g., content to review).
    /// </summary>
    [JsonPropertyName("requestData")]
    public required WorkflowMessage RequestData { get; init; }

    /// <summary>
    /// Human-readable title for UI.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// Human-readable description for UI.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// UI rendering hints (e.g., "approval_dialog", "text_input").
    /// </summary>
    [JsonPropertyName("uiHints")]
    public Dictionary<string, string>? UIHints { get; init; }

    [JsonPropertyName("requestedAt")]
    public DateTimeOffset RequestedAt { get; init; }
}

/// <summary>
/// Record of a workflow artifact (generated content).
/// </summary>
public sealed class WorkflowArtifactRecord
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("contentType")]
    public required string ContentType { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Error information for a failed workflow.
/// </summary>
public sealed class WorkflowErrorInfo
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; init; }
}

/// <summary>
/// Checkpoint data for workflow resumption.
/// </summary>
public sealed class WorkflowCheckpointData
{
    [JsonPropertyName("checkpointId")]
    public required string CheckpointId { get; init; }

    [JsonPropertyName("data")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required for binary JSON serialization")]
    public required byte[] Data { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Information about a workflow definition.
/// </summary>
public sealed class WorkflowDefinitionInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("inputTypeName")]
    public string? InputTypeName { get; init; }

    [JsonPropertyName("requestPorts")]
    public IReadOnlyList<RequestPortDefinition>? RequestPorts { get; init; }
}

/// <summary>
/// Definition of a RequestPort in a workflow.
/// </summary>
public sealed class RequestPortDefinition
{
    [JsonPropertyName("portId")]
    public required string PortId { get; init; }

    [JsonPropertyName("requestTypeName")]
    public required string RequestTypeName { get; init; }

    [JsonPropertyName("responseTypeName")]
    public required string ResponseTypeName { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

// ============ Request Types ============

/// <summary>
/// Request to start a new workflow.
/// </summary>
public sealed class StartWorkflowRequest
{
    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("input")]
    public required WorkflowMessage Input { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }

    [JsonPropertyName("options")]
    public Dictionary<string, string>? Options { get; init; }
}

/// <summary>
/// Request to list workflows.
/// </summary>
public sealed class ListWorkflowsRequest
{
    [JsonPropertyName("status")]
    public WorkflowRunStatus? Status { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 20;

    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("before")]
    public string? Before { get; init; }
}

/// <summary>
/// Request to execute a workflow (Gateway → AgentHost).
/// </summary>
public sealed class WorkflowExecutionRequest
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("input")]
    public required WorkflowMessage Input { get; init; }

    [JsonPropertyName("callbackBaseUrl")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "String required for JSON serialization")]
    public required string CallbackBaseUrl { get; init; }

    [JsonPropertyName("options")]
    public Dictionary<string, string>? Options { get; init; }
}

/// <summary>
/// Request to resume a workflow (Gateway → AgentHost).
/// </summary>
public sealed class WorkflowResumeRequest
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("callbackBaseUrl")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "String required for JSON serialization")]
    public required string CallbackBaseUrl { get; init; }

    [JsonPropertyName("signal")]
    public required WorkflowSignal Signal { get; init; }

    [JsonPropertyName("checkpointData")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required for binary JSON serialization")]
    public byte[]? CheckpointData { get; init; }
}

/// <summary>
/// Request to abort a workflow.
/// </summary>
public sealed class AbortWorkflowRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

// ============ State Update Types (AgentHost → Gateway) ============

/// <summary>
/// Status update for a workflow run.
/// </summary>
public sealed class WorkflowRunStatusUpdate
{
    [JsonPropertyName("status")]
    public required WorkflowRunStatus Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("error")]
    public WorkflowErrorInfo? Error { get; init; }
}

/// <summary>
/// Record that a step started.
/// </summary>
public sealed class WorkflowStepStartedRecord
{
    [JsonPropertyName("stepId")]
    public required string StepId { get; init; }

    [JsonPropertyName("executorId")]
    public required string ExecutorId { get; init; }

    [JsonPropertyName("executorName")]
    public string? ExecutorName { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Record that a step completed.
/// </summary>
public sealed class WorkflowStepCompletedRecord
{
    [JsonPropertyName("stepId")]
    public required string StepId { get; init; }

    [JsonPropertyName("executorId")]
    public required string ExecutorId { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset CompletedAt { get; init; }

    [JsonPropertyName("output")]
    public WorkflowMessage? Output { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }
}

// ============ Generic List Response ============

/// <summary>
/// Generic list response for paginated workflow results.
/// </summary>
public sealed class WorkflowListResponse<T>
{
    /// <summary>
    /// The object type, always "list".
    /// </summary>
    [JsonPropertyName("object")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Matches OpenAI API convention")]
    public string Object => "list";

    /// <summary>
    /// The list of items.
    /// </summary>
    [JsonPropertyName("data")]
    public required List<T> Data { get; init; }

    /// <summary>
    /// The ID of the first item in the list.
    /// </summary>
    [JsonPropertyName("firstId")]
    public string? FirstId { get; init; }

    /// <summary>
    /// The ID of the last item in the list.
    /// </summary>
    [JsonPropertyName("lastId")]
    public string? LastId { get; init; }

    /// <summary>
    /// Whether there are more items available.
    /// </summary>
    [JsonPropertyName("hasMore")]
    public required bool HasMore { get; init; }
}
