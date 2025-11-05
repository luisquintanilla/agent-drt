// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using AgentContracts;
using Microsoft.Agents.AI;

namespace AgentWebChat.AgentHost;

internal static class AgentFrameworkWebApplicationExtensions
{
    public static void MapAgentDiscovery(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string path)
    {
        var registeredAIAgents = endpoints.ServiceProvider.GetKeyedServices<AIAgent>(KeyedService.AnyKey);

        var routeGroup = endpoints.MapGroup(path);
        routeGroup.MapGet("/", async (CancellationToken cancellationToken) =>
        {
            var results = new List<AgentDiscoveryCard>();
            foreach (var result in registeredAIAgents)
            {
                results.Add(new AgentDiscoveryCard
                {
                    Name = result.Name!,
                    Description = result.Description,
                });
            }

            return Results.Ok(results);
        })
        .WithName("GetAgents");
    }
}
