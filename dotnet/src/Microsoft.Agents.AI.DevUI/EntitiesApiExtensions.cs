// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.DevUI.Entities;
using Microsoft.Agents.AI.Workflows;

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Provides extension methods for mapping entity discovery and management endpoints to an <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class EntitiesApiExtensions
{
    /// <summary>
    /// Maps HTTP API endpoints for entity discovery and management.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the routes to.</param>
    /// <returns>The <see cref="IEndpointRouteBuilder"/> for method chaining.</returns>
    /// <remarks>
    /// This extension method registers the following endpoints:
    /// <list type="bullet">
    /// <item><description>GET /v1/entities - List all registered entities (agents and workflows)</description></item>
    /// <item><description>GET /v1/entities/{entityId}/info - Get detailed information about a specific entity</description></item>
    /// </list>
    /// The endpoints are compatible with the Python DevUI frontend and automatically discover entities
    /// from the registered <see cref="AIAgent">agents</see> and <see cref="Workflow">workflows</see> in the dependency injection container,
    /// as well as any registered <see cref="IEntityProvider"/> services.
    /// </remarks>
    public static IEndpointConventionBuilder MapEntities(this IEndpointRouteBuilder endpoints)
    {
        // Get registered providers
        var providers = endpoints.ServiceProvider.GetServices<IEntityProvider>().ToList();
        // Add the default provider at the beginning so it takes precedence
        providers.Insert(0, new RegisteredServicesEntityProvider(endpoints.ServiceProvider));

        var group = endpoints.MapGroup("/v1/entities")
            .WithTags("Entities");

        // List all entities
        group.MapGet("", (CancellationToken cancellationToken)
                => ListEntitiesAsync(providers, cancellationToken))
            .WithName("ListEntities")
            .WithSummary("List all registered entities (agents and workflows)")
            .Produces<DiscoveryResponse>(StatusCodes.Status200OK, contentType: "application/json");

        // Get detailed entity information
        group.MapGet("{entityId}/info", (string entityId, string? type, CancellationToken cancellationToken)
                => GetEntityInfoAsync(entityId, type, providers, cancellationToken))
            .WithName("GetEntityInfo")
            .WithSummary("Get detailed information about a specific entity")
            .Produces<EntityInfo>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ListEntitiesAsync(
        IEnumerable<IEntityProvider> providers,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = new Dictionary<string, EntityInfo>();

            foreach (var provider in providers)
            {
                await foreach (var entity in provider.GetEntitiesAsync(cancellationToken))
                {
                    // First writer wins (respects provider order)
                    if (!entities.ContainsKey(entity.Id))
                    {
                        entities[entity.Id] = entity;
                    }
                }
            }

            return Results.Json(new DiscoveryResponse([.. entities.Values.OrderBy(e => e.Id)]), EntitiesJsonContext.Default.DiscoveryResponse);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error listing entities");
        }
    }

    private static async Task<IResult> GetEntityInfoAsync(
        string entityId,
        string? type,
        IEnumerable<IEntityProvider> providers,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var provider in providers)
            {
                var entity = await provider.GetEntityAsync(entityId, cancellationToken);
                if (entity != null)
                {
                    // If type filter is provided, check it
                    if (type != null && !string.Equals(entity.Type, type, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return Results.Json(entity, EntitiesJsonContext.Default.EntityInfo);
                }
            }

            return Results.NotFound(new { error = new { message = $"Entity '{entityId}' not found.", type = "invalid_request_error" } });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error getting entity info");
        }
    }
}
