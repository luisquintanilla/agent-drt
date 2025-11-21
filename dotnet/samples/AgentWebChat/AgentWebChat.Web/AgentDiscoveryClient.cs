// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI.DevUI.Entities;

namespace AgentWebChat.Web;

public class AgentDiscoveryClient(HttpClient httpClient, ILogger<AgentDiscoveryClient> logger)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<EntityInfo>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(new Uri("/v1/entities", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var discoveryResponse = JsonSerializer.Deserialize<DiscoveryResponse>(json, s_jsonOptions);
        var agents = discoveryResponse?.Entities ?? [];

        logger.LogInformation("Retrieved {AgentCount} agents from the API", agents.Count);
        return agents;
    }
}
