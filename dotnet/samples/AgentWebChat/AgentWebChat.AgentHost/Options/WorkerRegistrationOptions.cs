// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel.DataAnnotations;

namespace AgentWebChat.AgentHost.Options;

/// <summary>
/// Options for worker registration with the Agent Gateway.
/// </summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>
    /// Base address (absolute URI) of the Agent Gateway service.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Worker:GatewayBaseAddress configuration is required")]
    public string GatewayBaseAddress { get; set; } = string.Empty;

    /// <summary>
    /// Heartbeat interval in seconds. Default is 10s if not configured.
    /// </summary>
    [Range(1, 3600)]
    public int HeartbeatIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Explicit host identifier override. If not supplied, DNS host name is used.
    /// </summary>
    public string? HostId { get; set; }

    /// <summary>
    /// Explicitly configured advertised base address (e.g. https://myhost:1234). If not supplied the system will attempt to derive one from Kestrel listen endpoints.
    /// </summary>
    public string? AdvertisedBaseAddress { get; set; }
}
