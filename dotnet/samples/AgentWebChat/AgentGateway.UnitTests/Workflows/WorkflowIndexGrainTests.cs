// Copyright (c) Microsoft. All rights reserved.

using AgentContracts.Workflows;
using AgentGateway.Workflows;

namespace AgentGateway.UnitTests.Workflows;

/// <summary>
/// Unit tests for <see cref="WorkflowIndexGrain"/>.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public sealed class WorkflowIndexGrainTests
{
    private readonly OrleansTestClusterFixture _fixture;

    public WorkflowIndexGrainTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;
    }

    private IWorkflowIndexGrain GetIndexGrain() => this._fixture.GrainFactory.GetGrain<IWorkflowIndexGrain>("default");

    private static WorkflowRunSummary CreateSummary(
        string id,
        string workflowName = "TestWorkflow",
        WorkflowRunStatus status = WorkflowRunStatus.Queued,
        int pendingRequestCount = 0)
    {
        return new WorkflowRunSummary
        {
            Id = id,
            WorkflowName = workflowName,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            PendingRequestCount = pendingRequestCount
        };
    }

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_AddsWorkflowToIndex()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"reg-test-{Guid.NewGuid():N}";
        var summary = CreateSummary(runId, "RegisterTestWorkflow");

        // Act
        await grain.RegisterAsync(summary, CancellationToken.None);

        // Assert
        var result = await grain.GetAsync(runId, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Id.Should().Be(runId);
        result.WorkflowName.Should().Be("RegisterTestWorkflow");
        result.Status.Should().Be(WorkflowRunStatus.Queued);
    }

    [Fact]
    public async Task RegisterAsync_MultipleWorkflows_AllAreIndexed()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var baseId = $"multi-{Guid.NewGuid():N}";
        var ids = Enumerable.Range(1, 3).Select(i => $"{baseId}-{i}").ToList();

        // Act
        foreach (var id in ids)
        {
            await grain.RegisterAsync(CreateSummary(id), CancellationToken.None);
        }

        // Assert
        foreach (var id in ids)
        {
            var result = await grain.GetAsync(id, CancellationToken.None);
            result.Should().NotBeNull();
            result!.Id.Should().Be(id);
        }
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_UpdatesStatus()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"update-test-{Guid.NewGuid():N}";
        await grain.RegisterAsync(CreateSummary(runId), CancellationToken.None);

        // Act
        await grain.UpdateAsync(runId, WorkflowRunStatus.Running, 0, CancellationToken.None);

        // Assert
        var result = await grain.GetAsync(runId, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Status.Should().Be(WorkflowRunStatus.Running);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesPendingRequestCount()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"pending-test-{Guid.NewGuid():N}";
        await grain.RegisterAsync(CreateSummary(runId), CancellationToken.None);

        // Act
        await grain.UpdateAsync(runId, WorkflowRunStatus.WaitingForSignal, 2, CancellationToken.None);

        // Assert
        var result = await grain.GetAsync(runId, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Status.Should().Be(WorkflowRunStatus.WaitingForSignal);
        result.PendingRequestCount.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_SetsCompletedAt_ForTerminalStatus()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"complete-test-{Guid.NewGuid():N}";
        await grain.RegisterAsync(CreateSummary(runId), CancellationToken.None);

        // Act
        await grain.UpdateAsync(runId, WorkflowRunStatus.Completed, 0, CancellationToken.None);

        // Assert
        var result = await grain.GetAsync(runId, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Status.Should().Be(WorkflowRunStatus.Completed);
        result.CompletedAt.Should().NotBeNull();
        result.CompletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateAsync_DoesNotThrow_WhenWorkflowNotFound()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"not-found-{Guid.NewGuid():N}";

        // Act - should not throw
        await grain.UpdateAsync(runId, WorkflowRunStatus.Running, 0, CancellationToken.None);

        // Assert - workflow should still not exist
        var result = await grain.GetAsync(runId, CancellationToken.None);
        result.Should().BeNull();
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"nonexistent-{Guid.NewGuid():N}";

        // Act
        var result = await grain.GetAsync(runId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsSummary_WhenFound()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"get-test-{Guid.NewGuid():N}";
        var summary = CreateSummary(runId, "GetTestWorkflow");
        await grain.RegisterAsync(summary, CancellationToken.None);

        // Act
        var result = await grain.GetAsync(runId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(runId);
        result.WorkflowName.Should().Be("GetTestWorkflow");
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_ReturnsEmptyList_WhenNoWorkflows()
    {
        // Arrange - use a unique grain key to ensure isolation
        var grain = this._fixture.GrainFactory.GetGrain<IWorkflowIndexGrain>($"empty-{Guid.NewGuid():N}");

        // Act
        var result = await grain.ListAsync(null, 10, null, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
        result.FirstId.Should().BeNull();
        result.LastId.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsWorkflows_InReverseChronologicalOrder()
    {
        // Arrange - use a unique grain key
        var grain = this._fixture.GrainFactory.GetGrain<IWorkflowIndexGrain>($"order-{Guid.NewGuid():N}");

        foreach (var id in new[] { "first", "second", "third" })
        {
            await grain.RegisterAsync(CreateSummary(id), CancellationToken.None);
            await Task.Delay(10); // Small delay to ensure ordering
        }

        // Act
        var result = await grain.ListAsync(null, 10, null, null, CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(3);
        // Newest first
        result.Data[0].Id.Should().Be("third");
        result.Data[1].Id.Should().Be("second");
        result.Data[2].Id.Should().Be("first");
    }

    [Fact]
    public async Task ListAsync_RespectsLimit()
    {
        // Arrange - use a unique grain key
        var grain = this._fixture.GrainFactory.GetGrain<IWorkflowIndexGrain>($"limit-{Guid.NewGuid():N}");

        for (int i = 1; i <= 5; i++)
        {
            await grain.RegisterAsync(CreateSummary($"workflow-{i}"), CancellationToken.None);
        }

        // Act
        var result = await grain.ListAsync(null, 2, null, null, CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(2);
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_FiltersByStatus()
    {
        // Arrange - use a unique grain key
        var grain = this._fixture.GrainFactory.GetGrain<IWorkflowIndexGrain>($"filter-{Guid.NewGuid():N}");

        await grain.RegisterAsync(CreateSummary("queued-1", status: WorkflowRunStatus.Queued), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("running-1", status: WorkflowRunStatus.Running), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("queued-2", status: WorkflowRunStatus.Queued), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("completed-1", status: WorkflowRunStatus.Completed), CancellationToken.None);

        // Act
        var result = await grain.ListAsync(WorkflowRunStatus.Queued, 10, null, null, CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(2);
        result.Data.Should().OnlyContain(s => s.Status == WorkflowRunStatus.Queued);
    }

    [Fact]
    public async Task ListAsync_SupportsPaginationWithAfter()
    {
        // Arrange - use a unique grain key
        var grain = this._fixture.GrainFactory.GetGrain<IWorkflowIndexGrain>($"after-{Guid.NewGuid():N}");

        await grain.RegisterAsync(CreateSummary("first"), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("second"), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("third"), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("fourth"), CancellationToken.None);

        // Act - get items after "third" (which is at index 1 since newest first)
        var result = await grain.ListAsync(null, 10, "third", null, CancellationToken.None);

        // Assert - should get "second" and "first" (items after "third" in the list)
        result.Data.Should().HaveCount(2);
        result.Data[0].Id.Should().Be("second");
        result.Data[1].Id.Should().Be("first");
    }

    [Fact]
    public async Task ListAsync_ReturnsFirstIdAndLastId()
    {
        // Arrange - use a unique grain key
        var grain = this._fixture.GrainFactory.GetGrain<IWorkflowIndexGrain>($"ids-{Guid.NewGuid():N}");

        await grain.RegisterAsync(CreateSummary("first"), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("second"), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("third"), CancellationToken.None);

        // Act
        var result = await grain.ListAsync(null, 10, null, null, CancellationToken.None);

        // Assert
        result.FirstId.Should().Be("third"); // Newest first
        result.LastId.Should().Be("first");  // Oldest last
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_RemovesWorkflowFromIndex()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"remove-test-{Guid.NewGuid():N}";
        await grain.RegisterAsync(CreateSummary(runId), CancellationToken.None);

        // Verify it exists
        var beforeRemove = await grain.GetAsync(runId, CancellationToken.None);
        beforeRemove.Should().NotBeNull();

        // Act
        await grain.RemoveAsync(runId, CancellationToken.None);

        // Assert
        var afterRemove = await grain.GetAsync(runId, CancellationToken.None);
        afterRemove.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenWorkflowNotFound()
    {
        // Arrange
        var grain = this.GetIndexGrain();
        var runId = $"remove-nonexistent-{Guid.NewGuid():N}";

        // Act - should not throw
        await grain.RemoveAsync(runId, CancellationToken.None);

        // Assert - no exception means success
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromListResults()
    {
        // Arrange - use a unique grain key
        var grain = this._fixture.GrainFactory.GetGrain<IWorkflowIndexGrain>($"remove-list-{Guid.NewGuid():N}");

        await grain.RegisterAsync(CreateSummary("keep-1"), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("remove-me"), CancellationToken.None);
        await grain.RegisterAsync(CreateSummary("keep-2"), CancellationToken.None);

        // Act
        await grain.RemoveAsync("remove-me", CancellationToken.None);

        // Assert
        var result = await grain.ListAsync(null, 10, null, null, CancellationToken.None);
        result.Data.Should().HaveCount(2);
        result.Data.Should().NotContain(s => s.Id == "remove-me");
    }

    #endregion

    #region Integration with WorkflowGrain Tests

    [Fact]
    public async Task WorkflowGrain_StartAsync_RegistersInIndex()
    {
        // Arrange
        var runId = $"integration-{Guid.NewGuid():N}";
        var workflowGrain = this._fixture.GrainFactory.GetGrain<IWorkflowGrain>(runId);
        var indexGrain = this.GetIndexGrain();

        // Act
        await workflowGrain.StartAsync(
            new StartWorkflowRequest
            {
                WorkflowName = "IntegrationTestWorkflow",
                Input = WorkflowMessage.Create(new { test = true })
            },
            CancellationToken.None);

        // Assert
        var summary = await indexGrain.GetAsync(runId, CancellationToken.None);
        summary.Should().NotBeNull();
        summary!.WorkflowName.Should().Be("IntegrationTestWorkflow");
        summary.Status.Should().Be(WorkflowRunStatus.Queued);
    }

    [Fact]
    public async Task WorkflowGrain_UpdateStatus_UpdatesIndex()
    {
        // Arrange
        var runId = $"status-update-{Guid.NewGuid():N}";
        var workflowGrain = this._fixture.GrainFactory.GetGrain<IWorkflowGrain>(runId);
        var indexGrain = this.GetIndexGrain();

        await workflowGrain.StartAsync(
            new StartWorkflowRequest
            {
                WorkflowName = "StatusUpdateWorkflow",
                Input = WorkflowMessage.Create(new { test = true })
            },
            CancellationToken.None);

        // Act
        await workflowGrain.UpdateStatusAsync(
            new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Completed },
            null,
            CancellationToken.None);

        // Assert
        var summary = await indexGrain.GetAsync(runId, CancellationToken.None);
        summary.Should().NotBeNull();
        summary!.Status.Should().Be(WorkflowRunStatus.Completed);
    }

    [Fact]
    public async Task WorkflowGrain_RecordPendingRequest_UpdatesIndexPendingCount()
    {
        // Arrange
        var runId = $"pending-count-{Guid.NewGuid():N}";
        var workflowGrain = this._fixture.GrainFactory.GetGrain<IWorkflowGrain>(runId);
        var indexGrain = this.GetIndexGrain();

        await workflowGrain.StartAsync(
            new StartWorkflowRequest
            {
                WorkflowName = "PendingCountWorkflow",
                Input = WorkflowMessage.Create(new { test = true })
            },
            CancellationToken.None);

        // Act
        await workflowGrain.RecordPendingRequestAsync(
            new PendingExternalRequest
            {
                RequestId = "req-1",
                PortId = "approval",
                RequestTypeName = "ApprovalRequest",
                ResponseTypeName = "ApprovalResponse",
                RequestData = WorkflowMessage.Create(new { content = "test" }),
                RequestedAt = DateTimeOffset.UtcNow
            },
            null,
            CancellationToken.None);

        // Assert
        var summary = await indexGrain.GetAsync(runId, CancellationToken.None);
        summary.Should().NotBeNull();
        summary!.Status.Should().Be(WorkflowRunStatus.WaitingForSignal);
        summary.PendingRequestCount.Should().Be(1);
    }

    #endregion
}
