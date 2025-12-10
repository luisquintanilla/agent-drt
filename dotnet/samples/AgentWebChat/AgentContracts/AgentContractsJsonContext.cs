// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentContracts.Workflows;

namespace AgentContracts;

/// <summary>
/// Source generated JSON serialization context for the AgentHost project (normalized worker registration schema).
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = false)]
// Worker registration types
[JsonSerializable(typeof(WorkerRegistrationRequest))]
[JsonSerializable(typeof(WorkerRegistrationResponse))]
[JsonSerializable(typeof(WorkerProcessMetadata))]
// Workflow types
[JsonSerializable(typeof(WorkflowMessage))]
[JsonSerializable(typeof(WorkflowSignal))]
[JsonSerializable(typeof(WorkflowRun))]
[JsonSerializable(typeof(WorkflowRunSummary))]
[JsonSerializable(typeof(WorkflowStepInfo))]
[JsonSerializable(typeof(PendingExternalRequest))]
[JsonSerializable(typeof(WorkflowArtifactRecord))]
[JsonSerializable(typeof(WorkflowErrorInfo))]
[JsonSerializable(typeof(WorkflowCheckpointData))]
[JsonSerializable(typeof(WorkflowDefinitionInfo))]
[JsonSerializable(typeof(RequestPortDefinition))]
[JsonSerializable(typeof(StartWorkflowRequest))]
[JsonSerializable(typeof(ListWorkflowsRequest))]
[JsonSerializable(typeof(WorkflowExecutionRequest))]
[JsonSerializable(typeof(WorkflowResumeRequest))]
[JsonSerializable(typeof(AbortWorkflowRequest))]
[JsonSerializable(typeof(WorkflowRunStatusUpdate))]
[JsonSerializable(typeof(WorkflowStepStartedRecord))]
[JsonSerializable(typeof(WorkflowStepCompletedRecord))]
[JsonSerializable(typeof(WorkflowListResponse<WorkflowRunSummary>))]
[JsonSerializable(typeof(WorkflowCheckpointResult))]
// Workflow events (polymorphic)
[JsonSerializable(typeof(WorkflowStatusEvent))]
[JsonSerializable(typeof(WorkflowStartedEvent))]
[JsonSerializable(typeof(WorkflowStepStartedEvent))]
[JsonSerializable(typeof(WorkflowStepCompletedEvent))]
[JsonSerializable(typeof(WorkflowSignalRequestedEvent))]
[JsonSerializable(typeof(WorkflowSignalReceivedEvent))]
[JsonSerializable(typeof(WorkflowArtifactCreatedEvent))]
[JsonSerializable(typeof(WorkflowCompletedEvent))]
[JsonSerializable(typeof(WorkflowFailedEvent))]
[JsonSerializable(typeof(WorkflowCancelledEvent))]
[JsonSerializable(typeof(WorkflowAbortedEvent))]
[ExcludeFromCodeCoverage]
public sealed partial class AgentContractsJsonContext : JsonSerializerContext;
