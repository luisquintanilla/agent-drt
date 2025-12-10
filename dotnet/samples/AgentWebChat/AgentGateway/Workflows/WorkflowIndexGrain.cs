// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts.Workflows;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace AgentGateway.Workflows;

/// <summary>
/// State for the workflow index grain.
/// </summary>
[GenerateSerializer]
internal sealed class WorkflowIndexState
{
    /// <summary>
    /// Dictionary of run ID to workflow summary, ordered by creation time (newest first).
    /// </summary>
    [Id(0)]
    public Dictionary<string, WorkflowRunSummary> Runs { get; set; } = [];

    /// <summary>
    /// List of run IDs ordered by creation time (newest first).
    /// </summary>
    [Id(1)]
    public List<string> OrderedRunIds { get; set; } = [];
}

/// <summary>
/// Orleans grain that maintains an index of all workflow runs.
/// This is a singleton grain that enables listing and filtering workflows.
/// </summary>
internal sealed class WorkflowIndexGrain : Grain, IWorkflowIndexGrain
{
    private readonly ILogger<WorkflowIndexGrain> _logger;
    private readonly IPersistentState<WorkflowIndexState> _state;

    public WorkflowIndexGrain(
        [PersistentState("workflowIndex", "Default")] IPersistentState<WorkflowIndexState> state,
        ILogger<WorkflowIndexGrain> logger)
    {
        _state = state;
        _logger = logger;
    }

    public async Task RegisterAsync(WorkflowRunSummary summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);

        _state.State.Runs[summary.Id] = summary;

        // Insert at the beginning (newest first)
        _state.State.OrderedRunIds.Insert(0, summary.Id);

        await _state.WriteStateAsync(cancellationToken);
        _logger.LogDebug("Registered workflow {RunId} in index", summary.Id);
    }

    public async Task UpdateAsync(string runId, WorkflowRunStatus status, int pendingRequestCount, CancellationToken cancellationToken)
    {
        if (!_state.State.Runs.TryGetValue(runId, out var existing))
        {
            _logger.LogWarning("Attempted to update non-existent workflow {RunId} in index", runId);
            return;
        }

        _state.State.Runs[runId] = new WorkflowRunSummary
        {
            Id = existing.Id,
            WorkflowName = existing.WorkflowName,
            Status = status,
            CreatedAt = existing.CreatedAt,
            CompletedAt = status is WorkflowRunStatus.Completed or WorkflowRunStatus.Cancelled or WorkflowRunStatus.Aborted or WorkflowRunStatus.Failed
                ? DateTimeOffset.UtcNow
                : existing.CompletedAt,
            PendingRequestCount = pendingRequestCount,
            ETag = existing.ETag
        };

        await _state.WriteStateAsync(cancellationToken);
        _logger.LogDebug("Updated workflow {RunId} status to {Status} in index", runId, status);
    }

    public Task<WorkflowListResponse<WorkflowRunSummary>> ListAsync(
        WorkflowRunStatus? statusFilter,
        int limit,
        string? after,
        string? before,
        CancellationToken cancellationToken)
    {
        var query = _state.State.OrderedRunIds.AsEnumerable();

        // Apply cursor-based pagination
        if (!string.IsNullOrEmpty(after))
        {
            var afterIndex = _state.State.OrderedRunIds.IndexOf(after);
            if (afterIndex >= 0)
            {
                query = query.Skip(afterIndex + 1);
            }
        }

        if (!string.IsNullOrEmpty(before))
        {
            var beforeIndex = _state.State.OrderedRunIds.IndexOf(before);
            if (beforeIndex >= 0)
            {
                query = query.Take(beforeIndex);
            }
        }

        // Get summaries and apply status filter
        var summaries = query
            .Select(id => _state.State.Runs.GetValueOrDefault(id))
            .Where(s => s is not null)
            .Cast<WorkflowRunSummary>();

        if (statusFilter.HasValue)
        {
            summaries = summaries.Where(s => s.Status == statusFilter.Value);
        }

        var results = summaries.Take(limit + 1).ToList();
        var hasMore = results.Count > limit;

        if (hasMore)
        {
            results = results.Take(limit).ToList();
        }

        var response = new WorkflowListResponse<WorkflowRunSummary>
        {
            Data = results,
            FirstId = results.Count > 0 ? results[0].Id : null,
            LastId = results.Count > 0 ? results[^1].Id : null,
            HasMore = hasMore
        };

        return Task.FromResult(response);
    }

    public Task<WorkflowRunSummary?> GetAsync(string runId, CancellationToken cancellationToken)
    {
        _state.State.Runs.TryGetValue(runId, out var summary);
        return Task.FromResult(summary);
    }

    public async Task RemoveAsync(string runId, CancellationToken cancellationToken)
    {
        if (_state.State.Runs.Remove(runId))
        {
            _state.State.OrderedRunIds.Remove(runId);
            await _state.WriteStateAsync(cancellationToken);
            _logger.LogDebug("Removed workflow {RunId} from index", runId);
        }
    }
}
