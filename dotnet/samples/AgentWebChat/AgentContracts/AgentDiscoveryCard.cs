// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgentContracts;

/// <summary>
/// Represents metadata about an agent that can be discovered via the agent discovery endpoint.
/// </summary>
public sealed class AgentDiscoveryCard
{
    /// <summary>
    /// Gets or sets the name of the agent.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the description of the agent.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}
