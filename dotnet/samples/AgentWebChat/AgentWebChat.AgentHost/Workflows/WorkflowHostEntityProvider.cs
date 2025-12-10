// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.DevUI.Entities;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// Entity provider that exposes HITL workflows registered with the <see cref="WorkflowHostService"/>.
/// </summary>
internal sealed class WorkflowHostEntityProvider : IEntityProvider
{
    private readonly WorkflowHostService _workflowHost;

    public WorkflowHostEntityProvider(WorkflowHostService workflowHost)
    {
        _workflowHost = workflowHost;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<EntityInfo> GetEntitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var workflows = await _workflowHost.GetAvailableWorkflowsAsync(cancellationToken);

        foreach (var workflow in workflows)
        {
            yield return CreateEntityInfo(workflow);
        }
    }

    /// <inheritdoc/>
    public async Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var workflows = await _workflowHost.GetAvailableWorkflowsAsync(cancellationToken);

        var workflow = workflows.FirstOrDefault(w =>
            string.Equals(w.Name, entityId, StringComparison.OrdinalIgnoreCase));

        return workflow != null ? CreateEntityInfo(workflow) : null;
    }

    private static EntityInfo CreateEntityInfo(AgentContracts.Workflows.WorkflowDefinitionInfo workflow)
    {
        // Create input schema for ContentRequest
        var inputSchema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["topic"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The topic for the marketing content" },
                ["targetAudience"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The target audience for the content" },
                ["tone"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The desired tone of the content" }
            }
        };

        return new EntityInfo(
            Id: workflow.Name,
            Type: "workflow",
            Name: workflow.DisplayName ?? workflow.Name,
            Description: workflow.Description,
            Framework: "agent_framework",
            Tools: [],
            Metadata: []
        )
        {
            Source = "in_memory",
            Executors = ["ai-writer", "ai-reviewer", "human-approval"],
            InputSchema = JsonSerializer.SerializeToElement(inputSchema),
            InputTypeName = "ContentRequest",
            StartExecutorId = "ai-writer"
        };
    }
}
