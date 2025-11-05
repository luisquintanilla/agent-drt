// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json.Serialization;

namespace AgentContracts;

/// <summary>
/// Response returned from the /worker/meta endpoint providing a unique process instance identifier and logical host id.
/// </summary>
public sealed class WorkerProcessMetadata
{
    [JsonPropertyName("instanceId")]
    public Guid InstanceId { get; init; }

    [JsonPropertyName("hostId")]
    public string HostId { get; init; } = string.Empty;
}
