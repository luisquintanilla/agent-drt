// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentContracts.Workflows;
using Orleans;

namespace AgentGateway.Workflows;

/// <summary>
/// Orleans grain interface for maintaining an index of all workflow runs.
/// This is a singleton grain (key = "default") that tracks all workflow runs for list operations.
/// </summary>
internal interface IWorkflowIndexGrain : IGrainWithStringKey
{
    /// <summary>
    /// Registers a new workflow run in the index.
    /// </summary>
    Task RegisterAsync(WorkflowRunSummary summary, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a workflow run's status in the index.
    /// </summary>
    Task UpdateAsync(string runId, WorkflowRunStatus status, int pendingRequestCount, CancellationToken cancellationToken);

    /// <summary>
    /// Lists workflow runs with optional filtering and pagination.
    /// </summary>
    Task<WorkflowListResponse<WorkflowRunSummary>> ListAsync(
        WorkflowRunStatus? statusFilter,
        int limit,
        string? after,
        string? before,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a single workflow summary by ID.
    /// </summary>
    Task<WorkflowRunSummary?> GetAsync(string runId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a workflow run from the index.
    /// </summary>
    Task RemoveAsync(string runId, CancellationToken cancellationToken);
}
