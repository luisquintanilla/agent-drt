// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentGateway.DevUI.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentGateway.DevUI;

/// <summary>
/// Provides extension methods for mapping entity discovery and management endpoints to an <see cref="IEndpointRouteBuilder"/>.
/// </summary>
internal static class EntitiesApiExtensions
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
    /// from the registered agent and workflow catalogs.
    /// </remarks>
    public static IEndpointConventionBuilder MapEntities(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/entities")
            .WithTags("Entities");

        // List all entities
        group.MapGet("", ListEntitiesAsync)
            .WithName("ListEntities")
            .WithSummary("List all registered entities (agents and workflows)")
            .Produces<DiscoveryResponse>(StatusCodes.Status200OK, contentType: "application/json");

        // Get detailed entity information
        group.MapGet("{entityId}/info", GetEntityInfoAsync)
            .WithName("GetEntityInfo")
            .WithSummary("Get detailed information about a specific entity")
            .Produces<EntityInfo>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ListEntitiesAsync(
        WorkerRegistryEntityProvider entityProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await entityProvider.GetEntitiesAsync(cancellationToken).ToListAsync(cancellationToken).ConfigureAwait(false);
            return Results.Json(new DiscoveryResponse(entities), EntitiesJsonContext.Default.DiscoveryResponse);
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
        WorkerRegistryEntityProvider entityProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await entityProvider.GetEntityAsync(entityId, cancellationToken).ConfigureAwait(false);
            if (entity is not null)
            {
                Results.Json(entity, EntitiesJsonContext.Default.EntityInfo);
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
