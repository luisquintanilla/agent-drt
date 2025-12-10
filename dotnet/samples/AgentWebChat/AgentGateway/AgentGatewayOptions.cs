// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;

namespace AgentGateway;

/// <summary>
/// Configuration options for the Agent Gateway.
/// </summary>
public sealed class AgentGatewayOptions
{
    /// <summary>
    /// Configuration section name for the Agent Gateway options.
    /// </summary>
    public const string SectionName = "AgentGateway";

    /// <summary>
    /// Gets or sets the list of workers to register at startup.
    /// The first worker in the list is treated as the default worker and is assumed to always be available and support any workload.
    /// Can be specified as an array of endpoint strings or an array of worker configuration objects.
    /// </summary>
    public List<WorkerOptions> Workers { get; set; } = new();

    /// <summary>
    /// Gets or sets whether runtime worker registration is enabled.
    /// When false, the worker management API endpoints will not be mapped.
    /// Default is true.
    /// </summary>
    public bool EnableRuntimeRegistration { get; set; } = true;

    /// <summary>
    /// Gets or sets the base URL that workers should use to call back to the Gateway.
    /// This is used by workflows to report state updates.
    /// If not set, it will be inferred from the incoming request context.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "String required for JSON configuration binding")]
    public string? CallbackBaseUrl { get; set; }
}

/// <summary>
/// Configuration for a worker.
/// </summary>
public sealed class WorkerOptions
{
    /// <summary>
    /// Gets or sets the base endpoint URL for the worker.
    /// This is required when configuring a worker.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the host identifier for the worker.
    /// If not set, defaults to "worker-{position}" where position is 1-based.
    /// </summary>
    public string? HostId { get; set; }

    /// <summary>
    /// Gets or sets the relative path to the health check endpoint.
    /// Default is "/health".
    /// </summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>
    /// Gets or sets the relative path to the discovery endpoint.
    /// Default is "/agents".
    /// </summary>
    public string DiscoveryPath { get; set; } = "/v1/entities";
}
