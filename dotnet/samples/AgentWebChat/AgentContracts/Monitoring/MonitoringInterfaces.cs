// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentContracts.Monitoring;

/// <summary>
/// Service interface for retrieving monitoring data.
/// </summary>
public interface IMonitoringService
{
    /// <summary>
    /// Gets the current system status overview.
    /// </summary>
    Task<SystemStatus> GetSystemStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of all registered workers.
    /// </summary>
    Task<IReadOnlyList<WorkerStatus>> GetWorkersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a specific worker.
    /// </summary>
    Task<WorkerStatus?> GetWorkerAsync(string workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all currently active (running or waiting) workflows.
    /// </summary>
    Task<IReadOnlyList<WorkflowMonitoringSummary>> GetActiveWorkflowsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent workflows (completed, failed, cancelled, etc.).
    /// </summary>
    Task<IReadOnlyList<WorkflowMonitoringSummary>> GetRecentWorkflowsAsync(int count = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated workflow metrics for a time window.
    /// </summary>
    Task<WorkflowMetricsSnapshot> GetWorkflowMetricsAsync(TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams real-time monitoring events.
    /// </summary>
    IAsyncEnumerable<MonitoringEvent> StreamEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drains a worker (stops accepting new workflows).
    /// </summary>
    Task<bool> DrainWorkerAsync(string workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enables a drained worker.
    /// </summary>
    Task<bool> EnableWorkerAsync(string workerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for broadcasting monitoring events.
/// </summary>
public interface IMonitoringEventBroadcaster
{
    /// <summary>
    /// Publishes a monitoring event to all subscribers.
    /// </summary>
    void PublishEvent(MonitoringEvent evt);

    /// <summary>
    /// Publishes a workflow event.
    /// </summary>
    void PublishWorkflowEvent(string eventType, WorkflowEventPayload payload);

    /// <summary>
    /// Publishes a worker event.
    /// </summary>
    void PublishWorkerEvent(string eventType, WorkerEventPayload payload);

    /// <summary>
    /// Subscribes to monitoring events.
    /// </summary>
    IAsyncEnumerable<MonitoringEvent> SubscribeAsync(CancellationToken cancellationToken = default);
}
