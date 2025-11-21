// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.DevUI.Entities;

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Defines a provider for discovering entities (agents and workflows) to be exposed via the DevUI.
/// </summary>
public interface IEntityProvider
{
    /// <summary>
    /// Gets all entities discovered by this provider.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous stream of entity information.</returns>
    IAsyncEnumerable<EntityInfo> GetEntitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a specific entity.
    /// </summary>
    /// <param name="entityId">The ID of the entity to retrieve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity information, or null if not found.</returns>
    Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default);
}
