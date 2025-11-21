// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#if AGENTGATEWAY
using Microsoft.Agents.AI;
using AgentGateway.Conversations;
using AgentGateway.Responses.Models;

namespace AgentGateway.Responses;
#else
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;
#endif

/// <summary>
/// Response executor that routes requests to hosted AIAgent services based on agent.name or metadata["entity_id"].
/// This executor resolves agents from keyed services registered via AddAIAgent().
/// The model field is reserved for actual model names and is never used for entity/agent identification.
/// </summary>
public sealed class HostedAgentResponseExecutor : IResponseExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HostedAgentResponseExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedAgentResponseExecutor"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve hosted agents.</param>
    /// <param name="logger">The logger instance.</param>
    public HostedAgentResponseExecutor(
        IServiceProvider serviceProvider,
        ILogger<HostedAgentResponseExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this._serviceProvider = serviceProvider;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public ValueTask<ResponseError?> ValidateRequestAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default)
    {
        // Extract agent name from agent.name or model parameter
        string? agentName = GetAgentName(request);

        if (string.IsNullOrEmpty(agentName))
        {
            return ValueTask.FromResult<ResponseError?>(new ResponseError
            {
                Code = "missing_required_parameter",
                Message = "No 'agent.name' or 'metadata[\"entity_id\"]' specified in the request."
            });
        }

        // Validate that the agent can be resolved
        AIAgent? agent = this._serviceProvider.GetKeyedService<AIAgent>(agentName);
        if (agent is null)
        {
            this._logger.LogWarning("Failed to resolve agent with name '{AgentName}'", agentName);
            return ValueTask.FromResult<ResponseError?>(new ResponseError
            {
                Code = "agent_not_found",
                Message = $"""
                    Agent '{agentName}' not found.
                    Ensure the agent is registered with '{agentName}' name in the dependency injection container.
                    We recommend using 'builder.AddAIAgent()' for simplicity.
                """
            });
        }

        return ValueTask.FromResult<ResponseError?>(null);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamingResponseEvent> ExecuteAsync(
        AgentInvocationContext context,
        CreateResponse request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string agentName = GetAgentName(request)!;
        AIAgent agent = this._serviceProvider.GetRequiredKeyedService<AIAgent>(agentName);
        ResponsesAgentFeatureCollection features = new();
        ChatOptions chatOptions = new()
        {
            Temperature = (float?)request.Temperature,
            TopP = (float?)request.TopP,
            MaxOutputTokens = request.MaxOutputTokens,
            Instructions = request.Instructions,
            ModelId = request.Model,
        };
        features.Set(chatOptions);
        AgentRunOptions options = new()
        {
            Features = features,
        };

        if (!string.IsNullOrEmpty(context.ConversationId))
        {
            var conversationStorage = this._serviceProvider.GetService<IConversationStorage>();
            if (conversationStorage is not null)
            {
                ConversationStoreChatMessageStore messageStore = new(
                    conversationStorage,
                    context.ConversationId,
                    context.IdGenerator,
                    context.JsonSerializerOptions);

                features.Set<ChatMessageStore>(messageStore);
            }
            else
            {
                this._logger.LogWarning("IConversationStorage not available");
            }
        }
        else if (!string.IsNullOrEmpty(request.PreviousResponseId))
        {
            IResponseStorage? responseStorage = this._serviceProvider.GetService<IResponseStorage>();
            if (responseStorage is not null)
            {
                // Create a ChatMessageStore that uses the response storage to fetch the chain
                ResponseStoreChatMessageStore messageStore = new(
                    responseStorage,
                    request.PreviousResponseId,
                    context.JsonSerializerOptions);

                features.Set<ChatMessageStore>(messageStore);
            }
            else
            {
                this._logger.LogWarning("IResponseStorage not available to fetch previous response '{PreviousResponseId}'", request.PreviousResponseId);
            }
        }

        var inputMessages = request.Input.GetInputMessages().ConvertAll(x => x.ToChatMessage());
        await foreach (StreamingResponseEvent streamingEvent in agent.RunStreamingAsync(inputMessages, options: options, cancellationToken: cancellationToken)
            .ToStreamingResponseAsync(request, context, cancellationToken).ConfigureAwait(false))
        {
            yield return streamingEvent;
        }
    }

    /// <summary>
    /// Extracts the agent name for a request from the agent.name property, falling back to metadata["entity_id"].
    /// </summary>
    /// <param name="request">The create response request.</param>
    /// <returns>The agent name.</returns>
    private static string? GetAgentName(CreateResponse request)
    {
        string? agentName = request.Agent?.Name;

        // Fall back to metadata["entity_id"] if agent.name is not present
        if (string.IsNullOrEmpty(agentName) && request.Metadata?.TryGetValue("entity_id", out string? entityId) == true)
        {
            agentName = entityId;
        }

        return agentName;
    }

    private sealed class ResponsesAgentFeatureCollection : IAgentFeatureCollection
    {
        private readonly Dictionary<Type, object> _features = [];

        public object? this[Type key]
        {
            get => this._features[key];
            set
            {
                if (value is null)
                {
                    if (this._features.Remove(key, out _))
                    {
                        ++this.Revision;
                    }
                }
                else
                {
                    this._features[key] = value;
                    ++this.Revision;
                }
            }
        }

        public bool IsReadOnly { get; }
        public int Revision { get; private set; }

        public TFeature? Get<TFeature>()
        {
            this._features.TryGetValue(typeof(TFeature), out object? feature);
            return feature is TFeature typedFeature ? typedFeature : default;
        }

        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => this._features.GetEnumerator();

        public void Set<TFeature>(TFeature? instance) => this[typeof(TFeature)] = instance;

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
