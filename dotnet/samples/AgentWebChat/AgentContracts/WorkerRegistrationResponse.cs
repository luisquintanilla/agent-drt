// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgentContracts;

/// <summary>
/// Response from the Agent Gateway when registering a worker.
/// </summary>
public sealed class WorkerRegistrationResponse
{
    [JsonPropertyName("registrationId")]
    public required string RegistrationId { get; init; }
}
