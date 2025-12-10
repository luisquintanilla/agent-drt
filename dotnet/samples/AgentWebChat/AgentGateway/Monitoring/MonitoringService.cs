// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts.Monitoring;
using AgentContracts.Telemetry;
using AgentContracts.Workflows;
using AgentGateway.Workflows;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AgentGateway.Monitoring;

/// <summary>
/// Service for retrieving monitoring data from the system.
/// </summary>
internal sealed class MonitoringService : IMonitoringService
{
    private readonly IGrainFactory _grainFactory;
    private readonly WorkerRegistry _workerRegistry;
    private readonly WorkerDiscoveryCache _discoveryCache;
    private readonly IMonitoringEventBroadcaster _eventBroadcaster;
    private readonly ILogger<MonitoringService> _logger;
    private readonly DateTimeOffset _startTime;

    public MonitoringService(
        IGrainFactory grainFactory,
        WorkerRegistry workerRegistry,
        WorkerDiscoveryCache discoveryCache,
        IMonitoringEventBroadcaster eventBroadcaster,
        ILogger<MonitoringService> logger)
    {
        this._grainFactory = grainFactory;
        this._workerRegistry = workerRegistry;
        this._discoveryCache = discoveryCache;
        this._eventBroadcaster = eventBroadcaster;
        this._logger = logger;
        this._startTime = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<SystemStatus> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartMonitoringOperation("get_system_status");

        var indexGrain = this._grainFactory.GetGrain<IWorkflowIndexGrain>("default");

        // Get workflow counts by status
        var allWorkflows = await indexGrain.ListAsync(null, 1000, null, null, cancellationToken);
        var workflows = allWorkflows.Data;

        var activeCount = workflows.Count(w => w.Status == WorkflowRunStatus.Running);
        var queuedCount = workflows.Count(w => w.Status == WorkflowRunStatus.Queued);
        var waitingCount = workflows.Count(w => w.Status == WorkflowRunStatus.WaitingForSignal);

        var now = DateTimeOffset.UtcNow;
        var last24Hours = now.AddHours(-24);
        var completedLast24h = workflows.Count(w => w.Status == WorkflowRunStatus.Completed && w.CompletedAt >= last24Hours);
        var failedLast24h = workflows.Count(w => w.Status == WorkflowRunStatus.Failed && w.CompletedAt >= last24Hours);

        // Get worker counts
        var workers = this._workerRegistry.Entries.ToList();
        var healthyWorkers = workers.Count(w => !w.IsDown);
        const int drainedWorkers = 0; // TODO: Track drained state

        return new SystemStatus
        {
            Timestamp = now,
            ActiveWorkflows = activeCount,
            QueuedWorkflows = queuedCount,
            WaitingForSignalWorkflows = waitingCount,
            CompletedWorkflows24h = completedLast24h,
            FailedWorkflows24h = failedLast24h,
            RegisteredWorkers = workers.Count,
            HealthyWorkers = healthyWorkers,
            DrainedWorkers = drainedWorkers,
            Uptime = now - this._startTime,
            Version = GetVersion()
        };
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<WorkerStatus>> GetWorkersAsync(CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartMonitoringOperation("get_workers");

        var workers = this._workerRegistry.Entries.Select(entry => new WorkerStatus
        {
            WorkerId = entry.Info.Id,
            Address = entry.Info.Endpoint.ToString(),
            Health = entry.IsDown ? WorkerHealthState.Unhealthy : WorkerHealthState.Healthy,
            LastHealthCheck = entry.LastHeartbeat,
            RegisteredAt = entry.LastHeartbeat, // Approximation
            ActiveWorkflows = 0, // TODO: Track per-worker workflow counts
            SupportedWorkflows = this.GetSupportedWorkflows(entry.Info.Id),
            SupportedAgents = this.GetSupportedAgents(entry.Info.Id),
            IsDraining = false // TODO: Track drained state
        }).ToList();

        return Task.FromResult<IReadOnlyList<WorkerStatus>>(workers);
    }

    /// <inheritdoc/>
    public Task<WorkerStatus?> GetWorkerAsync(string workerId, CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartMonitoringOperation("get_worker");
        activity?.SetTag(TelemetryConstants.WorkerId, workerId);

        var entry = this._workerRegistry.Entries.FirstOrDefault(e => e.Info.Id == workerId);
        if (entry is null)
        {
            return Task.FromResult<WorkerStatus?>(null);
        }

        var status = new WorkerStatus
        {
            WorkerId = entry.Info.Id,
            Address = entry.Info.Endpoint.ToString(),
            Health = entry.IsDown ? WorkerHealthState.Unhealthy : WorkerHealthState.Healthy,
            LastHealthCheck = entry.LastHeartbeat,
            RegisteredAt = entry.LastHeartbeat,
            ActiveWorkflows = 0,
            SupportedWorkflows = this.GetSupportedWorkflows(entry.Info.Id),
            SupportedAgents = this.GetSupportedAgents(entry.Info.Id),
            IsDraining = false
        };

        return Task.FromResult<WorkerStatus?>(status);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowMonitoringSummary>> GetActiveWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartMonitoringOperation("get_active_workflows");

        var indexGrain = this._grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        var allWorkflows = await indexGrain.ListAsync(null, 1000, null, null, cancellationToken);

        var activeStatuses = new[] { WorkflowRunStatus.Running, WorkflowRunStatus.Queued, WorkflowRunStatus.WaitingForSignal, WorkflowRunStatus.Cancelling };

        return allWorkflows.Data
            .Where(w => activeStatuses.Contains(w.Status))
            .Select(w => new WorkflowMonitoringSummary
            {
                RunId = w.Id,
                WorkflowName = w.WorkflowName,
                Status = w.Status.ToString(),
                CreatedAt = w.CreatedAt,
                HasPendingSignal = w.PendingRequestCount > 0
            })
            .OrderByDescending(w => w.CreatedAt)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowMonitoringSummary>> GetRecentWorkflowsAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartMonitoringOperation("get_recent_workflows");

        var indexGrain = this._grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        var allWorkflows = await indexGrain.ListAsync(null, count, null, null, cancellationToken);

        return allWorkflows.Data
            .Select(w => new WorkflowMonitoringSummary
            {
                RunId = w.Id,
                WorkflowName = w.WorkflowName,
                Status = w.Status.ToString(),
                CreatedAt = w.CreatedAt,
                CompletedAt = w.CompletedAt,
                HasPendingSignal = w.PendingRequestCount > 0
            })
            .OrderByDescending(w => w.CreatedAt)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc/>
    public Task<WorkflowMetricsSnapshot> GetWorkflowMetricsAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartMonitoringOperation("get_workflow_metrics");

        var now = DateTimeOffset.UtcNow;

        // TODO: Implement actual metrics collection from the metrics store
        // For now, return placeholder data
        var snapshot = new WorkflowMetricsSnapshot
        {
            WindowStart = now - window,
            WindowEnd = now,
            WorkflowsStarted = 0,
            WorkflowsCompleted = 0,
            WorkflowsFailed = 0,
            AvgDurationMs = 0,
            P50DurationMs = 0,
            P95DurationMs = 0,
            P99DurationMs = 0,
            StepsExecuted = 0,
            AvgStepDurationMs = 0,
            SignalsProcessed = 0
        };

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<MonitoringEvent> StreamEventsAsync(CancellationToken cancellationToken = default)
    {
        return this._eventBroadcaster.SubscribeAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> DrainWorkerAsync(string workerId, CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartMonitoringOperation("drain_worker");
        activity?.SetTag(TelemetryConstants.WorkerId, workerId);

        // TODO: Implement worker draining
        this._logger.LogInformation("Drain requested for worker {WorkerId}", workerId);

        this._eventBroadcaster.PublishWorkerEvent(MonitoringEventTypes.WorkerDrained, new WorkerEventPayload
        {
            WorkerId = workerId
        });

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> EnableWorkerAsync(string workerId, CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartMonitoringOperation("enable_worker");
        activity?.SetTag(TelemetryConstants.WorkerId, workerId);

        // TODO: Implement worker enabling
        this._logger.LogInformation("Enable requested for worker {WorkerId}", workerId);

        this._eventBroadcaster.PublishWorkerEvent(MonitoringEventTypes.WorkerEnabled, new WorkerEventPayload
        {
            WorkerId = workerId
        });

        return Task.FromResult(true);
    }

    private List<string> GetSupportedWorkflows(string workerId)
    {
        var cached = this._discoveryCache.TryGet(workerId);
        return cached?.Entities?.Values
            .Where(e => string.Equals(e.Type, "workflow", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Name)
            .ToList() ?? [];
    }

    private List<string> GetSupportedAgents(string workerId)
    {
        var cached = this._discoveryCache.TryGet(workerId);
        return cached?.Entities?.Values
            .Where(e => string.Equals(e.Type, "agent", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Name)
            .ToList() ?? [];
    }

    private static string? GetVersion()
    {
        var assembly = typeof(MonitoringService).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString();
    }
}
