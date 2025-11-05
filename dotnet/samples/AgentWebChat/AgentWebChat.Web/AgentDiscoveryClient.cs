// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using AgentContracts;
using Microsoft.Agents.AI;

namespace AgentWebChat.Web;

public class AgentDiscoveryClient(HttpClient httpClient, ILogger<AgentDiscoveryClient> logger)
{
    private static readonly JsonSerializerOptions s_jsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(AgentAbstractionsJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Add(AgentContractsJsonContext.Default);
        options.MakeReadOnly();
        return options;
    }

    public async Task<List<AgentDiscoveryCard>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(new Uri("/agents", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var agents = JsonSerializer.Deserialize<List<AgentDiscoveryCard>>(json, s_jsonOptions) ?? [];

        logger.LogInformation("Retrieved {AgentCount} agents from the API", agents.Count);
        return agents;
    }
}
