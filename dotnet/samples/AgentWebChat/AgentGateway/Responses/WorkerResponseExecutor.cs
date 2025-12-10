// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.Logging;

namespace AgentGateway.Responses;

/// <summary>
/// Response executor that forwards requests to remote workers via HTTP.
/// Uses the worker registry and discovery cache to route requests to appropriate workers.
/// </summary>
internal sealed class WorkerResponseExecutor : IResponseExecutor
{
    private readonly WorkerRegistry _registry;
    private readonly WorkerDiscoveryCache _cache;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WorkerResponseExecutor> _logger;

    public WorkerResponseExecutor(
        WorkerRegistry registry,
        WorkerDiscoveryCache cache,
        IHttpClientFactory httpClientFactory,
        ILogger<WorkerResponseExecutor> logger)
    {
        this._registry = registry;
        this._cache = cache;
        this._httpClient = httpClientFactory.CreateClient();
        this._logger = logger;
    }

    public ValueTask<ResponseError?> ValidateRequestAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<ResponseError?>(null);

    public async IAsyncEnumerable<StreamingResponseEvent> ExecuteAsync(
        AgentInvocationContext context,
        CreateResponse request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agentName = GetAgentName(request);
        if (string.IsNullOrEmpty(agentName))
        {
            throw new InvalidOperationException("No 'agent.name' or 'model' specified in the request.");
        }

        // Select a worker that supports this agent
        // TODO: store worker assignment in grain state.
        var worker = await this.SelectWorkerAsync(agentName, cancellationToken);
        if (worker is null)
        {
            throw new InvalidOperationException($"No available worker supports agent '{agentName}'");
        }

        // Build the request URL - workers should have a /v1/responses endpoint
        var workerEndpoint = new Uri(worker.Endpoint, "/v1/responses");

        // Create HTTP request with the CreateResponse body
        // Force streaming to true since we want to stream events back
        var streamingRequest = request with { Stream = true };
        var json = JsonSerializer.Serialize(streamingRequest, AgentGatewayJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CreateResponse)));
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, workerEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Set the Response ID header so the worker can use it for persistence
        httpRequest.Headers.Add("X-Response-ID", context.ResponseId);

        this._logger.LogInformation(
            "Forwarding response {ResponseId} for agent {AgentName} to worker {WorkerId} at {WorkerEndpoint}",
            context.ResponseId, agentName, worker.Id, workerEndpoint);

        // Send the request and process the SSE stream
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await this._httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to forward request to worker {WorkerId}", worker.Id);
            throw new InvalidOperationException($"Failed to forward request to worker '{worker.Id}': {ex.Message}", ex);
        }

        // Read the SSE stream and deserialize events
        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            // SSE format: "data: {json}\n\n"
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line.Substring(6); // Skip "data: "

                // Deserialize the streaming event
                StreamingResponseEvent? streamingEvent;
                try
                {
                    streamingEvent = JsonSerializer.Deserialize<StreamingResponseEvent>(data, AgentGatewayJsonUtilities.DefaultOptions);
                }
                catch (Exception ex)
                {
                    this._logger.LogWarning(ex, "Failed to deserialize streaming event from worker: {Data}", data);
                    continue;
                }

                if (streamingEvent is not null)
                {
                    yield return streamingEvent;
                }
            }
        }
    }

    /// <summary>
    /// Select the best available worker (if any) from the registry that supports the given agent.
    /// </summary>
    private async ValueTask<WorkerRegistry.WorkerInfo?> SelectWorkerAsync(string? agentName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentName))
        {
            // If no specific agent is requested, return any available worker
            return this._registry.ActiveWorkers
                    .FirstOrDefault(w => w.DiscoveryPath is not null)
                ?? this._registry.ActiveWorkers.FirstOrDefault();
        }

        // Query each worker's discovery endpoint to find one that supports the specific agent
        foreach (var worker in this._registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
        {
            var entities = await this._cache.DiscoverEntitiesAsync(worker, cancellationToken);
            if (entities?.ContainsKey(agentName) == true)
            {
                return worker;
            }
        }

        // No worker found that supports this agent
        return null;
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
}
