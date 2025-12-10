// Copyright (c) Microsoft. All rights reserved.

using System;
using AgentContracts.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AgentGateway.Telemetry;

/// <summary>
/// Extension methods for configuring Gateway telemetry.
/// </summary>
public static class GatewayTelemetryExtensions
{
    /// <summary>
    /// Adds Gateway telemetry services including ActivitySource and Meters.
    /// </summary>
    public static IServiceCollection AddGatewayTelemetry(this IServiceCollection services)
    {
        // Register WorkflowMetrics as a singleton
        services.AddSingleton<WorkflowMetrics>();

        return services;
    }

    /// <summary>
    /// Configures OpenTelemetry tracing to include workflow activity sources.
    /// </summary>
    public static TracerProviderBuilder AddWorkflowInstrumentation(this TracerProviderBuilder builder)
    {
        return builder
            .AddSource(TelemetryConstants.WorkflowActivitySourceName)
            .AddSource(TelemetryConstants.MonitoringActivitySourceName);
    }

    /// <summary>
    /// Configures OpenTelemetry metrics to include workflow meters.
    /// </summary>
    public static MeterProviderBuilder AddWorkflowInstrumentation(this MeterProviderBuilder builder)
    {
        return builder
            .AddMeter(TelemetryConstants.WorkflowMeterName)
            .AddMeter(TelemetryConstants.WorkerMeterName);
    }
}
