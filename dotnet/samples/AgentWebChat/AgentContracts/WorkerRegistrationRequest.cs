// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AgentContracts;

/// <summary>
/// Registration payload sent from the Agent Host (worker) to the Agent Gateway using normalized schema.
/// </summary>
public sealed class WorkerRegistrationRequest
{
    [JsonPropertyName("hostId")]
    public string HostId { get; init; } = Environment.MachineName;

    // Base endpoint (scheme://host[:port])
    [JsonPropertyName("endpoint")]
    [Required(ErrorMessage = "Endpoint is required.")]
    [AbsoluteUri(ErrorMessage = "Endpoint must be a valid absolute URI.")]
    public required string Endpoint { get; init; }

    // Relative paths
    [JsonPropertyName("healthPath")]
    public string HealthPath { get; init; } = "/health";

    [JsonPropertyName("discoveryPath")]
    public string DiscoveryPath { get; init; } = "/discovery";
}
