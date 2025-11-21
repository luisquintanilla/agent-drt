// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI.DevUI.Entities;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// An entity provider that discovers agents and workflows registered in the dependency injection container.
/// </summary>
internal sealed class RegisteredServicesEntityProvider : IEntityProvider
{
    private readonly IServiceProvider _serviceProvider;

    public RegisteredServicesEntityProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<EntityInfo> GetEntitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agents = GetRegisteredEntities<AIAgent>(_serviceProvider);
        foreach (var agent in agents)
        {
            yield return CreateAgentEntityInfo(agent);
        }

        var workflows = GetRegisteredEntities<Workflow>(_serviceProvider);
        foreach (var workflow in workflows)
        {
            yield return CreateWorkflowEntityInfo(workflow);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var agents = GetRegisteredEntities<AIAgent>(_serviceProvider);
        foreach (var agent in agents)
        {
            var info = CreateAgentEntityInfo(agent);
            if (string.Equals(info.Id, entityId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<EntityInfo?>(info);
            }
        }

        var workflows = GetRegisteredEntities<Workflow>(_serviceProvider);
        foreach (var workflow in workflows)
        {
            var info = CreateWorkflowEntityInfo(workflow);
            if (string.Equals(info.Id, entityId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<EntityInfo?>(info);
            }
        }

        return Task.FromResult<EntityInfo?>(null);
    }

    private static IEnumerable<T> GetRegisteredEntities<T>(IServiceProvider serviceProvider)
    {
        var keyedEntities = serviceProvider.GetKeyedServices<T>(KeyedService.AnyKey);
        var defaultEntities = serviceProvider.GetServices<T>() ?? [];

        return keyedEntities
            .Concat(defaultEntities)
            .Where(entity => entity is not null);
    }

    private static EntityInfo CreateAgentEntityInfo(AIAgent agent)
    {
        var entityId = agent.Name ?? agent.Id;

        // Extract tools and other metadata using GetService
        List<string> tools = [];
        var metadata = new Dictionary<string, JsonElement>();

        // Try to get ChatOptions from the agent which may contain tools
        if (agent.GetService<ChatOptions>() is { Tools: { Count: > 0 } agentTools })
        {
            tools = agentTools
                .Where(tool => !string.IsNullOrWhiteSpace(tool.Name))
                .Select(tool => tool.Name!)
                .Distinct()
                .ToList();
        }

        // Extract agent-specific fields (top-level properties for compatibility with Python)
        string? instructions = null;
        string? modelId = null;
        string? chatClientType = null;

        // Get instructions from ChatClientAgent
        if (agent is ChatClientAgent chatAgent && !string.IsNullOrWhiteSpace(chatAgent.Instructions))
        {
            instructions = chatAgent.Instructions;
        }

        // Get IChatClient to extract metadata
        IChatClient? chatClient = agent.GetService<IChatClient>();
        if (chatClient != null)
        {
            // Get chat client type
            chatClientType = chatClient.GetType().Name;

            // Get model ID from ChatClientMetadata
            if (chatClient.GetService<ChatClientMetadata>() is { } chatClientMetadata)
            {
                modelId = chatClientMetadata.DefaultModelId;

                // Add additional metadata for compatibility
                if (!string.IsNullOrWhiteSpace(chatClientMetadata.ProviderName))
                {
                    metadata["chat_client_provider"] = JsonSerializer.SerializeToElement(chatClientMetadata.ProviderName, EntitiesJsonContext.Default.String);
                }

                if (chatClientMetadata.ProviderUri is not null)
                {
                    metadata["provider_uri"] = JsonSerializer.SerializeToElement(chatClientMetadata.ProviderUri.ToString(), EntitiesJsonContext.Default.String);
                }
            }
        }

        // Add provider name from AIAgentMetadata if available
        if (agent.GetService<AIAgentMetadata>() is { } agentMetadata && !string.IsNullOrWhiteSpace(agentMetadata.ProviderName))
        {
            metadata["provider_name"] = JsonSerializer.SerializeToElement(agentMetadata.ProviderName, EntitiesJsonContext.Default.String);
        }

        // Add agent type information to metadata (in addition to chat_client_type)
        var agentTypeName = agent.GetType().Name;
        metadata["agent_type"] = JsonSerializer.SerializeToElement(agentTypeName, EntitiesJsonContext.Default.String);

        return new EntityInfo(
            Id: entityId,
            Type: "agent",
            Name: agent.DisplayName,
            Description: agent.Description,
            Framework: "agent_framework",
            Tools: tools,
            Metadata: metadata
        )
        {
            Source = "in_memory",
            Instructions = instructions,
            ModelId = modelId,
            ChatClientType = chatClientType,
            Executors = [],  // Agents have empty executors list (workflows use this field)
        };
    }

    private static EntityInfo CreateWorkflowEntityInfo(Workflow workflow)
    {
        // Extract executor IDs from the workflow structure
        var executorIds = new HashSet<string> { workflow.StartExecutorId };
        var reflectedEdges = workflow.ReflectEdges();
        foreach (var (sourceId, edgeSet) in reflectedEdges)
        {
            executorIds.Add(sourceId);
            foreach (var edge in edgeSet)
            {
                foreach (var sinkId in edge.Connection.SinkIds)
                {
                    executorIds.Add(sinkId);
                }
            }
        }

        // Create a default input schema (string type)
        var defaultInputSchema = new Dictionary<string, string>
        {
            ["type"] = "string"
        };

        var workflowId = workflow.Name ?? workflow.StartExecutorId;
        return new EntityInfo(
            Id: workflowId,
            Type: "workflow",
            Name: workflowId,
            Description: workflow.Description,
            Framework: "agent_framework",
            Tools: [],
            Metadata: []
        )
        {
            Source = "in_memory",
            Executors = [.. executorIds],  // Workflows use Executors instead of Tools
            WorkflowDump = JsonSerializer.SerializeToElement(
                workflow.ToDevUIDict(),
                EntitiesJsonContext.Default.DictionaryStringJsonElement),
            InputSchema = JsonSerializer.SerializeToElement(defaultInputSchema, EntitiesJsonContext.Default.DictionaryStringString),
            InputTypeName = "string",
            StartExecutorId = workflow.StartExecutorId
        };
    }
}
