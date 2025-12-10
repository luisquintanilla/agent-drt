// Copyright (c) Microsoft. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentContracts;
using AgentContracts.Workflows;
using AgentGateway.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Storage;

namespace AgentGateway.UnitTests.Workflows;

/// <summary>
/// Integration tests for the WorkflowHttpApi endpoints.
/// Uses ASP.NET Core TestServer with an in-memory Orleans cluster.
/// </summary>
public sealed class WorkflowHttpApiIntegrationTests : IAsyncDisposable
{
    private WebApplication? _app;
    private HttpClient? _httpClient;

    private static readonly JsonSerializerOptions s_jsonOptions = AgentContractsJsonUtilities.DefaultOptions;

    /// <summary>
    /// Creates a URI from a relative path.
    /// </summary>
    private static Uri CreateUri(string relativePath) => new(relativePath, UriKind.Relative);

    #region Setup

    private async Task<HttpClient> CreateTestServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Configure System.Text.Json serialization for Orleans grain method parameters
        // This is required for types like StartWorkflowRequest, WorkflowSignal, etc.
        // NOTE: Exceptions are excluded to allow Orleans to use its built-in exception serialization
        builder.Services.AddSerializer(serializerBuilder =>
        {
            serializerBuilder.AddJsonSerializer(
                isSupported: type => !typeof(Exception).IsAssignableFrom(type) &&
                                    (type.Namespace?.StartsWith("Microsoft.Agents", StringComparison.Ordinal) == true ||
                                     type.Namespace?.StartsWith("AgentContracts", StringComparison.Ordinal) == true ||
                                     type.Namespace?.StartsWith("AgentGateway", StringComparison.Ordinal) == true),
                jsonSerializerOptions: AgentGatewayJsonUtilities.DefaultOptions);
        });

        // Configure Orleans with in-memory storage
        builder.Host.UseOrleans(siloBuilder =>
        {
            siloBuilder.UseLocalhostClustering();
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.UseInMemoryReminderService();

            // Register System.Text.Json-based grain storage serializer
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer>(sp =>
                new Utilities.SystemTextJsonGrainStorageSerializer(AgentGatewayJsonUtilities.DefaultOptions));
        });

        _app = builder.Build();

        // Map workflow endpoints
        _app.MapWorkflows();

        await _app.StartAsync();

        var testServer = _app.Services.GetRequiredService<IServer>() as TestServer
            ?? throw new InvalidOperationException("TestServer not found");

        _httpClient = testServer.CreateClient();
        return _httpClient;
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        if (_app != null)
        {
            await _app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region List Workflows Tests

    [Fact]
    public async Task ListWorkflows_ReturnsEmptyList_WhenNoWorkflows()
    {
        // Arrange
        var client = await CreateTestServerAsync();

        // Act
        var response = await client.GetAsync(CreateUri("/v1/workflows"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowListResponse<WorkflowRunSummary>>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task ListWorkflows_ReturnsWorkflows_AfterCreation()
    {
        // Arrange
        var client = await CreateTestServerAsync();

        // Create a workflow
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/workflows"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowListResponse<WorkflowRunSummary>>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().HaveCount(1);
        result.Data[0].WorkflowName.Should().Be("TestWorkflow");
    }

    [Fact]
    public async Task ListWorkflows_FiltersByStatus()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var grainFactory = _app!.Services.GetRequiredService<IGrainFactory>();

        // Create workflows with different statuses
        var request1 = new StartWorkflowRequest { WorkflowName = "Workflow1", Input = WorkflowMessage.Create(new { id = 1 }) };
        var response1 = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request1, s_jsonOptions);
        var run1 = await response1.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var request2 = new StartWorkflowRequest { WorkflowName = "Workflow2", Input = WorkflowMessage.Create(new { id = 2 }) };
        await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request2, s_jsonOptions);

        // Update first workflow to Running
        var grain1 = grainFactory.GetGrain<IWorkflowGrain>(run1!.Id);
        await grain1.UpdateStatusAsync(new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running }, null, CancellationToken.None);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/workflows?status=Running"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowListResponse<WorkflowRunSummary>>(s_jsonOptions);
        result!.Data.Should().HaveCount(1);
        result.Data[0].Status.Should().Be(WorkflowRunStatus.Running);
    }

    #endregion

    #region Start Workflow Tests

    [Fact]
    public async Task StartWorkflow_CreatesWorkflow_Returns201()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var request = new StartWorkflowRequest
        {
            WorkflowName = "MarketingContentWorkflow",
            Input = WorkflowMessage.Create(new { topic = "AI", audience = "Developers" }),
            Metadata = new Dictionary<string, string> { ["source"] = "test" }
        };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Id.Should().StartWith("wfrun_");
        result.WorkflowName.Should().Be("MarketingContentWorkflow");
        result.Status.Should().Be(WorkflowRunStatus.Queued);
        result.Metadata.Should().ContainKey("source");
        result.ETag.Should().NotBeNullOrEmpty();

        // Location header should be set
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain(result.Id);
    }

    #endregion

    #region Get Workflow Tests

    [Fact]
    public async Task GetWorkflow_ReturnsWorkflow_WhenExists()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Act
        var response = await client.GetAsync(CreateUri($"/v1/workflows/{created!.Id}"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.WorkflowName.Should().Be("TestWorkflow");
    }

    [Fact]
    public async Task GetWorkflow_Returns404_WhenNotExists()
    {
        // Arrange
        var client = await CreateTestServerAsync();

        // Act
        var response = await client.GetAsync(CreateUri("/v1/workflows/nonexistent-id"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Send Signal Tests

    [Fact]
    public async Task SendSignal_ResumesWorkflow_WhenPendingRequestExists()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var grainFactory = _app!.Services.GetRequiredService<IGrainFactory>();

        // Create workflow
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "HITLWorkflow",
            Input = WorkflowMessage.Create(new { content = "test" })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Add pending request via grain (simulating agent host behavior)
        var grain = grainFactory.GetGrain<IWorkflowGrain>(created!.Id);
        await grain.RecordPendingRequestAsync(
            new PendingExternalRequest
            {
                RequestId = "req-1",
                PortId = "approval",
                RequestTypeName = "ApprovalRequest",
                ResponseTypeName = "ApprovalResponse",
                RequestData = WorkflowMessage.Create(new { content = "Review this" }),
                Title = "Approve Content",
                RequestedAt = DateTimeOffset.UtcNow
            },
            null,
            CancellationToken.None);

        var signal = new WorkflowSignal
        {
            RequestId = "req-1",
            Response = WorkflowMessage.Create(new { approved = true, feedback = "Looks good!" })
        };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{created.Id}/signals"), signal, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Status.Should().Be(WorkflowRunStatus.Running);
        result.PendingRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendSignal_Returns400_WhenRequestNotFound()
    {
        // Arrange
        var client = await CreateTestServerAsync();

        // Create workflow
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var signal = new WorkflowSignal
        {
            RequestId = "nonexistent-request",
            Response = WorkflowMessage.Create(new { approved = true })
        };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{created!.Id}/signals"), signal, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Cancel Workflow Tests

    [Fact]
    public async Task CancelWorkflow_SetsCancellingStatus()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Act
        var response = await client.PostAsync(CreateUri($"/v1/workflows/{created!.Id}/cancel"), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result!.Status.Should().Be(WorkflowRunStatus.Cancelling);
    }

    [Fact]
    public async Task CancelWorkflow_Returns409_WhenAlreadyTerminal()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var grainFactory = _app!.Services.GetRequiredService<IGrainFactory>();

        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Complete the workflow
        var grain = grainFactory.GetGrain<IWorkflowGrain>(created!.Id);
        await grain.UpdateStatusAsync(new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Completed }, null, CancellationToken.None);

        // Act
        var response = await client.PostAsync(CreateUri($"/v1/workflows/{created.Id}/cancel"), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region Abort Workflow Tests

    [Fact]
    public async Task AbortWorkflow_SetsAbortedStatus()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var abortRequest = new AbortWorkflowRequest { Reason = "Admin requested abort" };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{created!.Id}/abort"), abortRequest, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result!.Status.Should().Be(WorkflowRunStatus.Aborted);
        result.CompletedAt.Should().NotBeNull();
    }

    #endregion

    #region State API Tests (AgentHost callbacks)

    [Fact]
    public async Task UpdateStatus_UpdatesWorkflowStatus()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var statusUpdate = new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running };

        // Act
        var response = await client.PutAsJsonAsync(CreateUri($"/v1/workflows/{created!.Id}/state/status"), statusUpdate, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ETagResponse>(s_jsonOptions);
        result!.ETag.Should().NotBeNullOrEmpty();

        // Verify status changed
        var getResponse = await client.GetAsync(CreateUri($"/v1/workflows/{created.Id}"));
        var workflow = await getResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        workflow!.Status.Should().Be(WorkflowRunStatus.Running);
    }

    [Fact]
    public async Task UpdateStatus_Returns409_WhenETagMismatch()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var statusUpdate = new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running };

        using var request = new HttpRequestMessage(HttpMethod.Put, CreateUri($"/v1/workflows/{created!.Id}/state/status"))
        {
            Content = new StringContent(JsonSerializer.Serialize(statusUpdate, s_jsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("If-Match", "\"invalid-etag\"");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RecordStepStarted_AddsStepToWorkflow()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var step = new WorkflowStepStartedRecord
        {
            StepId = "step-1",
            ExecutorId = "writer-agent",
            ExecutorName = "Writer Agent",
            StartedAt = DateTimeOffset.UtcNow
        };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{created!.Id}/state/steps/started"), step, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify step was added
        var getResponse = await client.GetAsync(CreateUri($"/v1/workflows/{created.Id}"));
        var workflow = await getResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        workflow!.Steps.Should().HaveCount(1);
        workflow.Steps[0].StepId.Should().Be("step-1");
    }

    [Fact]
    public async Task RecordPendingRequest_SetsWaitingStatus()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var pendingRequest = new PendingExternalRequest
        {
            RequestId = "req-1",
            PortId = "approval",
            RequestTypeName = "ApprovalRequest",
            ResponseTypeName = "ApprovalResponse",
            RequestData = WorkflowMessage.Create(new { content = "Review this" }),
            Title = "Approve Content",
            RequestedAt = DateTimeOffset.UtcNow
        };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{created!.Id}/state/pending-requests"), pendingRequest, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify workflow is waiting
        var getResponse = await client.GetAsync(CreateUri($"/v1/workflows/{created.Id}"));
        var workflow = await getResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        workflow!.Status.Should().Be(WorkflowRunStatus.WaitingForSignal);
        workflow.PendingRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveAndGetCheckpoint_WorksCorrectly()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var checkpoint = new WorkflowCheckpointData
        {
            CheckpointId = "cp-1",
            Data = [1, 2, 3, 4, 5],
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act - Save checkpoint
        var saveResponse = await client.PutAsJsonAsync(CreateUri($"/v1/workflows/{created!.Id}/state/checkpoint"), checkpoint, s_jsonOptions);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Get checkpoint
        var getResponse = await client.GetAsync(CreateUri($"/v1/workflows/{created.Id}/state/checkpoint"));

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await getResponse.Content.ReadFromJsonAsync<WorkflowCheckpointResult>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Checkpoint.CheckpointId.Should().Be("cp-1");
        result.Checkpoint.Data.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });
        result.ETag.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetCheckpoint_Returns204_WhenNoCheckpoint()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Act
        var response = await client.GetAsync(CreateUri($"/v1/workflows/{created!.Id}/state/checkpoint"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RecordArtifact_AddsArtifactToWorkflow()
    {
        // Arrange
        var client = await CreateTestServerAsync();
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "TestWorkflow",
            Input = WorkflowMessage.Create(new { test = true })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var artifact = new WorkflowArtifactRecord
        {
            Id = "artifact-1",
            Name = "Generated Content",
            ContentType = "text/markdown",
            Content = "# Hello World",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{created!.Id}/state/artifacts"), artifact, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify artifact was added
        var getResponse = await client.GetAsync(CreateUri($"/v1/workflows/{created.Id}"));
        var workflow = await getResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        workflow!.Artifacts.Should().HaveCount(1);
        workflow.Artifacts[0].Id.Should().Be("artifact-1");
    }

    #endregion

    #region Full HITL Flow Test

    [Fact]
    public async Task FullHITLFlow_ViaHttpApi_WorksEndToEnd()
    {
        // Arrange
        var client = await CreateTestServerAsync();

        // Step 1: Start workflow
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "MarketingContentWorkflow",
            Input = WorkflowMessage.Create(new { topic = "AI in Healthcare", audience = "Executives" })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var workflow = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        var runId = workflow!.Id;

        // Step 2: Update to Running (simulating AgentHost)
        var statusUpdate = new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running };
        await client.PutAsJsonAsync(CreateUri($"/v1/workflows/{runId}/state/status"), statusUpdate, s_jsonOptions);

        // Step 3: Record step started
        var stepStarted = new WorkflowStepStartedRecord
        {
            StepId = "step-writer",
            ExecutorId = "writer-agent",
            ExecutorName = "Content Writer",
            StartedAt = DateTimeOffset.UtcNow
        };
        await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{runId}/state/steps/started"), stepStarted, s_jsonOptions);

        // Step 4: Record step completed
        var stepCompleted = new WorkflowStepCompletedRecord
        {
            StepId = "step-writer",
            ExecutorId = "writer-agent",
            CompletedAt = DateTimeOffset.UtcNow,
            Output = WorkflowMessage.Create(new { draft = "AI is transforming healthcare..." }),
            DurationMs = 3000
        };
        await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{runId}/state/steps/completed"), stepCompleted, s_jsonOptions);

        // Step 5: Record pending request (HITL)
        var pendingRequest = new PendingExternalRequest
        {
            RequestId = "approval-req-1",
            PortId = "human-approval",
            RequestTypeName = "ContentApprovalRequest",
            ResponseTypeName = "ContentApprovalResponse",
            RequestData = WorkflowMessage.Create(new { draft = "AI is transforming healthcare..." }),
            Title = "Approve Marketing Content",
            Description = "Please review and approve the marketing content",
            RequestedAt = DateTimeOffset.UtcNow
        };
        await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{runId}/state/pending-requests"), pendingRequest, s_jsonOptions);

        // Verify waiting state
        var getResponse = await client.GetAsync(CreateUri($"/v1/workflows/{runId}"));
        var waitingWorkflow = await getResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        waitingWorkflow!.Status.Should().Be(WorkflowRunStatus.WaitingForSignal);
        waitingWorkflow.PendingRequests.Should().HaveCount(1);

        // Step 6: Human approves via signal
        var signal = new WorkflowSignal
        {
            RequestId = "approval-req-1",
            Response = WorkflowMessage.Create(new { approved = true, feedback = "Great content!" })
        };
        var signalResponse = await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{runId}/signals"), signal, s_jsonOptions);
        signalResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var resumedWorkflow = await signalResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        resumedWorkflow!.Status.Should().Be(WorkflowRunStatus.Running);
        resumedWorkflow.PendingRequests.Should().BeEmpty();

        // Step 7: Record artifact
        var artifact = new WorkflowArtifactRecord
        {
            Id = "final-content-1",
            Name = "Final Marketing Content",
            ContentType = "text/markdown",
            Content = "AI is transforming healthcare...",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{runId}/state/artifacts"), artifact, s_jsonOptions);

        // Step 8: Complete workflow
        await client.PutAsJsonAsync(CreateUri($"/v1/workflows/{runId}/state/status"),
            new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Completed }, s_jsonOptions);

        // Final verification
        var finalResponse = await client.GetAsync(CreateUri($"/v1/workflows/{runId}"));
        var finalWorkflow = await finalResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        finalWorkflow!.Status.Should().Be(WorkflowRunStatus.Completed);
        finalWorkflow.CompletedAt.Should().NotBeNull();
        finalWorkflow.Steps.Should().HaveCount(1);
        finalWorkflow.Artifacts.Should().HaveCount(1);

        // Verify it appears in list
        var listResponse = await client.GetAsync(CreateUri("/v1/workflows?status=Completed"));
        var list = await listResponse.Content.ReadFromJsonAsync<WorkflowListResponse<WorkflowRunSummary>>(s_jsonOptions);
        list!.Data.Should().Contain(s => s.Id == runId && s.Status == WorkflowRunStatus.Completed);
    }

    #endregion
}

/// <summary>
/// Internal ETagResponse class for deserialization.
/// </summary>
file sealed class ETagResponse
{
    public required string ETag { get; init; }
}
