// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AgentContracts.Telemetry;
using AgentContracts.Workflows;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// Service for hosting and executing workflows.
/// Implements IWorkflowHost to handle workflow execution requests from the Gateway.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection")]
internal sealed class WorkflowHostService : IWorkflowHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowHostService> _logger;
    private readonly Dictionary<string, Type> _workflowRegistry = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowHostService"/> class.
    /// </summary>
    public WorkflowHostService(
        IServiceProvider serviceProvider,
        ILogger<WorkflowHostService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this._serviceProvider = serviceProvider;
        this._logger = logger;
    }

    /// <summary>
    /// Registers a workflow type with this host.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow type.</typeparam>
    /// <param name="name">The workflow name (used in API requests).</param>
    public void RegisterWorkflow<TWorkflow>(string name) where TWorkflow : IWorkflow
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this._workflowRegistry[name] = typeof(TWorkflow);
        this._logger.LogInformation("Registered workflow: {WorkflowName} -> {WorkflowType}", name, typeof(TWorkflow).Name);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<WorkflowStatusEvent> ExecuteAsync(
        WorkflowExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return this.ExecuteInternalAsync(request, cancellationToken);
    }

    private async IAsyncEnumerable<WorkflowStatusEvent> ExecuteInternalAsync(
        WorkflowExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartWorkflowExecution(request.RunId, request.WorkflowName, ActivityKind.Server);

        this._logger.LogInformation("Starting workflow execution: {RunId} (workflow: {WorkflowName})",
            request.RunId, request.WorkflowName);

        // Resolve workflow type
        if (!this._workflowRegistry.TryGetValue(request.WorkflowName, out var workflowType))
        {
            this._logger.LogError("Unknown workflow: {WorkflowName}", request.WorkflowName);
            WorkflowActivitySource.RecordException(activity, new InvalidOperationException($"Unknown workflow: '{request.WorkflowName}'"));
            yield return new WorkflowFailedEvent
            {
                RunId = request.RunId,
                SequenceNumber = 1,
                Timestamp = DateTimeOffset.UtcNow,
                Error = new WorkflowErrorInfo
                {
                    Code = "WORKFLOW_NOT_FOUND",
                    Message = $"Unknown workflow: '{request.WorkflowName}'"
                }
            };
            yield break;
        }

        // Create state client for callbacks
        var stateClient = CreateStateClient(request.CallbackBaseUrl);

        // Create execution context
        var context = new WorkflowExecutionContext(
            request.RunId,
            request.WorkflowName,
            request.Input,
            stateClient,
            request.Options,
            this._logger);

        // Use a channel to safely collect events from the workflow
        var channel = Channel.CreateUnbounded<WorkflowStatusEvent>();

        // Execute workflow in background and write to channel
        var executionTask = this.ExecuteWorkflowCoreAsync(
            request,
            workflowType,
            context,
            stateClient,
            channel.Writer,
            cancellationToken);

        // Read events from channel and yield them
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        // Ensure execution task completes (should already be done when channel closes)
        await executionTask;
    }

    private async Task ExecuteWorkflowCoreAsync(
        WorkflowExecutionRequest request,
        Type workflowType,
        WorkflowExecutionContext context,
        GatewayWorkflowStateClient stateClient,
        ChannelWriter<WorkflowStatusEvent> writer,
        CancellationToken cancellationToken)
    {
        IWorkflow? workflow = null;
        try
        {
            workflow = (IWorkflow)ActivatorUtilities.CreateInstance(this._serviceProvider, workflowType);

            // Update status to Running
            await stateClient.UpdateStatusAsync(
                request.RunId,
                new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running },
                etag: null,
                cancellationToken);

            // Execute workflow and forward events to channel
            await foreach (var evt in workflow.ExecuteAsync(context, cancellationToken))
            {
                await writer.WriteAsync(evt, cancellationToken);
            }

            // Update status to Completed
            await stateClient.UpdateStatusAsync(
                request.RunId,
                new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Completed },
                etag: null,
                cancellationToken);

            // Send completion event (Gateway will fill in the full Workflow object)
            await writer.WriteAsync(new WorkflowCompletedSignalEvent
            {
                RunId = request.RunId,
                SequenceNumber = 0, // Gateway will assign proper sequence numbers
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this._logger.LogInformation("Workflow cancelled: {RunId}", request.RunId);

            await this.SafeUpdateStatusAsync(stateClient, request.RunId, WorkflowRunStatus.Cancelled);

            await writer.WriteAsync(new WorkflowCancelledEvent
            {
                RunId = request.RunId,
                SequenceNumber = 0,
                Timestamp = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Workflow failed: {RunId}", request.RunId);

            var errorInfo = new WorkflowErrorInfo
            {
                Code = "WORKFLOW_EXECUTION_ERROR",
                Message = ex.Message,
                StackTrace = ex.StackTrace
            };

            await this.SafeUpdateStatusAsync(stateClient, request.RunId, WorkflowRunStatus.Failed, errorInfo);

            await writer.WriteAsync(new WorkflowFailedEvent
            {
                RunId = request.RunId,
                SequenceNumber = 0,
                Timestamp = DateTimeOffset.UtcNow,
                Error = errorInfo
            }, CancellationToken.None);
        }
        finally
        {
            // Close the channel to signal completion
            writer.Complete();

            if (workflow is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (workflow is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<WorkflowStatusEvent> ResumeAsync(
        WorkflowResumeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return this.ResumeInternalAsync(request, cancellationToken);
    }

    private async IAsyncEnumerable<WorkflowStatusEvent> ResumeInternalAsync(
        WorkflowResumeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = WorkflowActivitySource.StartWorkflowResume(request.RunId, request.WorkflowName, request.Signal.RequestId);

        this._logger.LogInformation("Resuming workflow: {RunId} with signal for request {RequestId}",
            request.RunId, request.Signal.RequestId);

        // Resolve workflow type
        if (!this._workflowRegistry.TryGetValue(request.WorkflowName, out var workflowType))
        {
            this._logger.LogError("Unknown workflow: {WorkflowName}", request.WorkflowName);
            WorkflowActivitySource.RecordException(activity, new InvalidOperationException($"Unknown workflow: '{request.WorkflowName}'"));
            yield return new WorkflowFailedEvent
            {
                RunId = request.RunId,
                SequenceNumber = 0,
                Timestamp = DateTimeOffset.UtcNow,
                Error = new WorkflowErrorInfo
                {
                    Code = "WORKFLOW_NOT_FOUND",
                    Message = $"Unknown workflow: '{request.WorkflowName}'"
                }
            };
            yield break;
        }

        // Create state client
        var stateClient = CreateStateClient(request.CallbackBaseUrl);

        // Use a channel to safely collect events
        var channel = Channel.CreateUnbounded<WorkflowStatusEvent>();

        // Execute resume in background and write to channel
        var resumeTask = this.ResumeWorkflowCoreAsync(
            request,
            workflowType,
            stateClient,
            channel.Writer,
            cancellationToken);

        // Read events from channel and yield them
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        // Ensure resume task completes
        await resumeTask;
    }

    private async Task ResumeWorkflowCoreAsync(
        WorkflowResumeRequest request,
        Type workflowType,
        GatewayWorkflowStateClient stateClient,
        ChannelWriter<WorkflowStatusEvent> writer,
        CancellationToken cancellationToken)
    {
        // Get checkpoint if available
        WorkflowCheckpointResult? checkpoint = null;
        if (request.CheckpointData is not null)
        {
            checkpoint = new WorkflowCheckpointResult
            {
                Checkpoint = new WorkflowCheckpointData
                {
                    CheckpointId = $"cp_{Guid.NewGuid():N}",
                    Data = request.CheckpointData,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                ETag = string.Empty // Not needed for resume
            };
        }

        IWorkflow? workflow = null;
        try
        {
            workflow = (IWorkflow)ActivatorUtilities.CreateInstance(this._serviceProvider, workflowType);

            // Create resume context
            var context = new WorkflowResumeContext(
                request.RunId,
                request.WorkflowName,
                request.Signal,
                checkpoint?.Checkpoint,
                stateClient,
                this._logger);

            // Update status to Running
            await stateClient.UpdateStatusAsync(
                request.RunId,
                new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running },
                etag: null,
                cancellationToken);

            // Clear the pending request
            await stateClient.ClearPendingRequestAsync(
                request.RunId,
                request.Signal.RequestId,
                etag: null,
                cancellationToken);

            // Resume workflow and forward events to channel
            await foreach (var evt in workflow.ResumeAsync(context, cancellationToken))
            {
                await writer.WriteAsync(evt, cancellationToken);
            }

            // Update status to Completed
            await stateClient.UpdateStatusAsync(
                request.RunId,
                new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Completed },
                etag: null,
                cancellationToken);

            // Send completion event
            await writer.WriteAsync(new WorkflowCompletedSignalEvent
            {
                RunId = request.RunId,
                SequenceNumber = 0,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this._logger.LogInformation("Workflow resume cancelled: {RunId}", request.RunId);

            await this.SafeUpdateStatusAsync(stateClient, request.RunId, WorkflowRunStatus.Cancelled);

            await writer.WriteAsync(new WorkflowCancelledEvent
            {
                RunId = request.RunId,
                SequenceNumber = 0,
                Timestamp = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Workflow resume failed: {RunId}", request.RunId);

            var errorInfo = new WorkflowErrorInfo
            {
                Code = "WORKFLOW_RESUME_ERROR",
                Message = ex.Message,
                StackTrace = ex.StackTrace
            };

            await this.SafeUpdateStatusAsync(stateClient, request.RunId, WorkflowRunStatus.Failed, errorInfo);

            await writer.WriteAsync(new WorkflowFailedEvent
            {
                RunId = request.RunId,
                SequenceNumber = 0,
                Timestamp = DateTimeOffset.UtcNow,
                Error = errorInfo
            }, CancellationToken.None);
        }
        finally
        {
            // Close the channel to signal completion
            writer.Complete();

            if (workflow is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (workflow is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
        }
    }

    private async Task SafeUpdateStatusAsync(
        GatewayWorkflowStateClient stateClient,
        string runId,
        WorkflowRunStatus status,
        WorkflowErrorInfo? error = null)
    {
        try
        {
            await stateClient.UpdateStatusAsync(
                runId,
                new WorkflowRunStatusUpdate
                {
                    Status = status,
                    Error = error
                },
                etag: null,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to update workflow status to {Status}: {RunId}", status, runId);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<WorkflowDefinitionInfo>> GetAvailableWorkflowsAsync(
        CancellationToken cancellationToken = default)
    {
        var workflows = this._workflowRegistry.Select(kvp => new WorkflowDefinitionInfo
        {
            Name = kvp.Key,
            DisplayName = kvp.Value.Name,
            Description = $"Workflow implemented by {kvp.Value.FullName}"
        }).ToList();

        return Task.FromResult<IReadOnlyList<WorkflowDefinitionInfo>>(workflows);
    }

    private static GatewayWorkflowStateClient CreateStateClient(string callbackBaseUrl)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(callbackBaseUrl)
        };
        return new GatewayWorkflowStateClient(httpClient);
    }
}

/// <summary>
/// Interface for workflow implementations.
/// </summary>
public interface IWorkflow
{
    /// <summary>
    /// Executes the workflow from the beginning.
    /// </summary>
    IAsyncEnumerable<WorkflowStatusEvent> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes the workflow after receiving a signal.
    /// </summary>
    IAsyncEnumerable<WorkflowStatusEvent> ResumeAsync(
        WorkflowResumeContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context provided to workflows during execution.
/// </summary>
public sealed class WorkflowExecutionContext
{
    /// <summary>
    /// The workflow run ID.
    /// </summary>
    public string RunId { get; }

    /// <summary>
    /// The workflow name.
    /// </summary>
    public string WorkflowName { get; }

    /// <summary>
    /// The input message.
    /// </summary>
    public WorkflowMessage Input { get; }

    /// <summary>
    /// The state service for recording progress.
    /// </summary>
    public IWorkflowStateService StateService { get; }

    /// <summary>
    /// Workflow execution options.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Options { get; }

    /// <summary>
    /// Logger for the workflow.
    /// </summary>
    public ILogger Logger { get; }

    internal WorkflowExecutionContext(
        string runId,
        string workflowName,
        WorkflowMessage input,
        IWorkflowStateService stateService,
        IReadOnlyDictionary<string, string>? options,
        ILogger logger)
    {
        this.RunId = runId;
        this.WorkflowName = workflowName;
        this.Input = input;
        this.StateService = stateService;
        this.Options = options;
        this.Logger = logger;
    }
}

/// <summary>
/// Context provided to workflows during resume.
/// </summary>
public sealed class WorkflowResumeContext
{
    /// <summary>
    /// The workflow run ID.
    /// </summary>
    public string RunId { get; }

    /// <summary>
    /// The workflow name.
    /// </summary>
    public string WorkflowName { get; }

    /// <summary>
    /// The signal that triggered the resume.
    /// </summary>
    public WorkflowSignal Signal { get; }

    /// <summary>
    /// The checkpoint data, if available.
    /// </summary>
    public WorkflowCheckpointData? Checkpoint { get; }

    /// <summary>
    /// The state service for recording progress.
    /// </summary>
    public IWorkflowStateService StateService { get; }

    /// <summary>
    /// Logger for the workflow.
    /// </summary>
    public ILogger Logger { get; }

    internal WorkflowResumeContext(
        string runId,
        string workflowName,
        WorkflowSignal signal,
        WorkflowCheckpointData? checkpoint,
        IWorkflowStateService stateService,
        ILogger logger)
    {
        this.RunId = runId;
        this.WorkflowName = workflowName;
        this.Signal = signal;
        this.Checkpoint = checkpoint;
        this.StateService = stateService;
        this.Logger = logger;
    }
}
