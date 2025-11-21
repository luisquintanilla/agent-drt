// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

#if AGENTGATEWAY
using AgentGateway.Models;
using AgentGateway.Responses.Converters;
using AgentGateway.Responses.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentGateway.Responses;
#else
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;
#endif

/// <summary>
/// In-memory implementation of responses service for testing and development.
/// This implementation is thread-safe but data is not persisted across application restarts.
/// Uses IResponseStorage for storing response state and metadata.
/// </summary>
public sealed class InMemoryResponsesService : IResponsesService, IDisposable
{
    private readonly IResponseExecutor _executor;
    private readonly IResponseStorage _storage;
    private readonly ConcurrentDictionary<string, ResponseState> _runningResponses = new();
    private readonly TimeSpan _completedResponseRetentionPeriod;
    private readonly ILogger<InMemoryResponsesService> _logger;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    private sealed class ResponseState(CreateResponse request)
    {
        private readonly object _lock = new();
        private TaskCompletionSource _updateSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<int, ItemResource> _outputItems = [];

        public CreateResponse Request { get; } = request;
        public List<StreamingResponseEvent> StreamingUpdates { get; } = [];
        public Task? CompletionTask { get; set; }
        public string? CurrentETag { get; set; }
        public bool IsTerminal { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; } = new();

        public void AddStreamingEvent(StreamingResponseEvent streamingEvent)
        {
            lock (this._lock)
            {
                this.StreamingUpdates.Add(streamingEvent);

                // Track output items as they're added or updated
                if (streamingEvent is StreamingOutputItemAdded itemAdded)
                {
                    this._outputItems[itemAdded.OutputIndex] = itemAdded.Item;
                }
                else if (streamingEvent is StreamingOutputItemDone itemDone)
                {
                    this._outputItems[itemDone.OutputIndex] = itemDone.Item;
                }

                // Check if we've reached a terminal state
                if (streamingEvent is IStreamingResponseEventWithResponse responseEvent)
                {
                    this.IsTerminal = responseEvent.Response.IsTerminal;
                }
            }

            this.SignalUpdate();
        }

        public List<ItemResource> GetOutputItems()
        {
            lock (this._lock)
            {
                return [.. this._outputItems.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value)];
            }
        }

        public async IAsyncEnumerable<StreamingResponseEvent> StreamUpdatesAsync(
            int startingAfter = 0,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int streamedCount = startingAfter;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Capture the wait task before checking state to avoid race conditions
                Task waitTask = this.WaitForUpdateAsync(cancellationToken);

                // Copy any new updates and check terminal state while holding the lock
                List<StreamingResponseEvent> newUpdates;
                bool isTerminal;
                lock (this._lock)
                {
                    newUpdates = this.StreamingUpdates.Skip(streamedCount).ToList();
                    streamedCount += newUpdates.Count;
                    isTerminal = this.IsTerminal;
                }

                // Yield the updates outside the lock
                foreach (StreamingResponseEvent update in newUpdates)
                {
                    yield return update;
                }

                // Check if we're done (after yielding any final events)
                if (isTerminal)
                {
                    break;
                }

                // Wait for the next update to be signaled
                await waitTask.ConfigureAwait(false);
            }
        }

        private Task WaitForUpdateAsync(CancellationToken cancellationToken)
        {
            Task signalTask = this._updateSignal.Task;
            return signalTask.WaitAsync(cancellationToken);
        }

        internal void SignalUpdate()
        {
            TaskCompletionSource oldSignal = Interlocked.Exchange(ref this._updateSignal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            oldSignal.TrySetResult();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryResponsesService"/> class.
    /// </summary>
    /// <param name="executor">The response executor to use.</param>
    /// <param name="storage">The response storage to use.</param>
    /// <param name="options">The service options.</param>
    /// <param name="logger">The logger instance.</param>
    public InMemoryResponsesService(
        IResponseExecutor executor,
        IResponseStorage storage,
        IOptions<ResponsesServiceOptions> options,
        ILogger<InMemoryResponsesService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(options);
        this._executor = executor;
        this._storage = storage;
        this._completedResponseRetentionPeriod = options.Value.CompletedResponseRetentionPeriod;
        this._logger = logger ?? NullLogger<InMemoryResponsesService>.Instance;

        // Start cleanup timer to run every minute
        this._cleanupTimer = new Timer(
            callback: this.CleanupCompletedResponses,
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    /// <inheritdoc />
    public async ValueTask<ResponseError?> ValidateRequestAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default)
    {
        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id) &&
            !string.IsNullOrEmpty(request.PreviousResponseId))
        {
            return new ResponseError
            {
                Code = "invalid_request",
                Message = "Mutually exclusive parameters: 'conversation' and 'previous_response_id'. Ensure you are only providing one of: 'previous_response_id' or 'conversation'."
            };
        }

        return await this._executor.ValidateRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Response> CreateResponseAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default)
    {
        if (request.Stream == true)
        {
            throw new InvalidOperationException("Cannot create a streaming response using CreateResponseAsync. Use CreateResponseStreamingAsync instead.");
        }

        IdGenerator idGenerator = new(responseId: null, conversationId: request.Conversation?.Id);
        string responseId = idGenerator.ResponseId;
        ResponseState state = await this.InitializeResponseAsync(responseId, request, cancellationToken).ConfigureAwait(false);
        CancellationToken ct = request.Background switch
        {
            true => CancellationToken.None,
            _ => cancellationToken,
        };
        state.CompletionTask = this.ExecuteResponseAsync(responseId, state, ct);

        // For background responses, start execution and return immediately
        if (request.Background == true)
        {
            StorageResult<Response>? result = await this._storage.GetResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
            return result?.Value ?? throw new InvalidOperationException($"Failed to retrieve response '{responseId}' after creation.");
        }

        // For non-background responses, wait for completion
        await state.CompletionTask!.WaitAsync(cancellationToken).ConfigureAwait(false);
        StorageResult<Response>? completedResult = await this._storage.GetResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
        return completedResult?.Value ?? throw new InvalidOperationException($"Failed to retrieve response '{responseId}' after completion.");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingResponseEvent> CreateResponseStreamingAsync(
        CreateResponse request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.Stream == false)
        {
            throw new InvalidOperationException("Cannot create a non-streaming response using CreateResponseStreamingAsync. Use CreateResponseAsync instead.");
        }

        var idGenerator = new IdGenerator(responseId: null, conversationId: request.Conversation?.Id);
        var responseId = idGenerator.ResponseId;
        var state = await this.InitializeResponseAsync(responseId, request, cancellationToken).ConfigureAwait(false);

        // Start execution
        state.CompletionTask = this.ExecuteResponseAsync(responseId, state, CancellationToken.None);

        // Stream updates as they become available
        await foreach (StreamingResponseEvent update in state.StreamUpdatesAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public async Task<Response?> GetResponseAsync(string responseId, CancellationToken cancellationToken = default)
    {
        StorageResult<Response>? result = await this._storage.GetResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
        return result?.Value;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingResponseEvent> GetResponseStreamingAsync(
        string responseId,
        int? startingAfter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!this._runningResponses.TryGetValue(responseId, out ResponseState? state))
        {
            yield break;
        }

        // Stream existing updates starting from the specified position
        await foreach (StreamingResponseEvent update in state.StreamUpdatesAsync(startingAfter ?? 0, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public async Task<Response> CancelResponseAsync(string responseId, CancellationToken cancellationToken = default)
    {
        StorageResult<Response>? result = await this._storage.GetResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            throw new InvalidOperationException($"Response '{responseId}' not found.");
        }

        if (result.Value.Background != true)
        {
            throw new InvalidOperationException($"Only background responses can be cancelled. Response '{responseId}' was not created with background=true.");
        }

        if (result.Value.IsTerminal)
        {
            throw new InvalidOperationException($"Response '{responseId}' is already in a terminal state and cannot be cancelled.");
        }

        // Cancel the execution
        if (!this._runningResponses.TryGetValue(responseId, out ResponseState? state))
        {
            throw new InvalidOperationException($"Response '{responseId}' is not running.");
        }

        state.CancellationTokenSource.Cancel();

        // Wait for the completion task if available
        if (state.CompletionTask is { } task)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        // Get the updated response
        result = await this._storage.GetResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
        return result?.Value ?? throw new InvalidOperationException($"Failed to retrieve response '{responseId}' after cancellation.");
    }

    /// <inheritdoc />
    public async Task<bool> DeleteResponseAsync(string responseId, CancellationToken cancellationToken = default)
    {
        bool deleted = await this._storage.DeleteResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
        if (deleted && this._runningResponses.TryRemove(responseId, out ResponseState? state))
        {
            state.CancellationTokenSource.Dispose();
        }
        return deleted;
    }

    /// <inheritdoc />
    public async Task<ListResponse<ItemResource>> ListResponseInputItemsAsync(
        string responseId,
        int? limit = null,
        SortOrder? order = null,
        string? after = null,
        string? before = null,
        CancellationToken cancellationToken = default)
    {
        int effectiveLimit = Math.Clamp(limit ?? IResponsesService.DefaultListLimit, 1, 100);
        SortOrder effectiveOrder = order ?? SortOrder.Descending;

        if (!this._runningResponses.TryGetValue(responseId, out ResponseState? state))
        {
            throw new InvalidOperationException($"Response '{responseId}' not found.");
        }

        List<ItemResource> itemResources = GetInputItems(responseId, state);

        // Apply ordering
        if (effectiveOrder == SortOrder.Descending)
        {
            itemResources.Reverse();
        }

        // Apply pagination
        IEnumerable<ItemResource> filtered = itemResources.AsEnumerable();

        if (!string.IsNullOrEmpty(after))
        {
            int afterIndex = itemResources.FindIndex(m => m.Id == after);
            if (afterIndex >= 0)
            {
                filtered = itemResources.Skip(afterIndex + 1);
            }
        }

        if (!string.IsNullOrEmpty(before))
        {
            int beforeIndex = itemResources.FindIndex(m => m.Id == before);
            if (beforeIndex >= 0)
            {
                filtered = filtered.Take(beforeIndex);
            }
        }

        List<ItemResource> result = filtered.Take(effectiveLimit + 1).ToList();
        bool hasMore = result.Count > effectiveLimit;
        if (hasMore)
        {
            result = result.Take(effectiveLimit).ToList();
        }

        return await Task.FromResult(new ListResponse<ItemResource>
        {
            Data = result,
            FirstId = result.FirstOrDefault()?.Id,
            LastId = result.LastOrDefault()?.Id,
            HasMore = hasMore
        }).ConfigureAwait(false);
    }

    private async Task<ResponseState> InitializeResponseAsync(string responseId, CreateResponse request, CancellationToken cancellationToken)
    {
        Dictionary<string, string> metadata = request.Metadata ?? [];

        // Create initial response
        // Background responses always start as "queued", non-background as "in_progress"
        ResponseStatus initialStatus = request.Background is true ? ResponseStatus.Queued : ResponseStatus.InProgress;
        Response response = new()
        {
            Agent = request.Agent?.ToAgentId(),
            Background = request.Background,
            Conversation = request.Conversation,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Error = null,
            Id = responseId,
            IncompleteDetails = null,
            Instructions = request.Instructions,
            MaxOutputTokens = request.MaxOutputTokens,
            MaxToolCalls = request.MaxToolCalls,
            Metadata = metadata,
            Model = request.Model,
            Output = [],
            ParallelToolCalls = request.ParallelToolCalls ?? true,
            PreviousResponseId = request.PreviousResponseId,
            Prompt = request.Prompt,
            PromptCacheKey = request.PromptCacheKey,
            Reasoning = request.Reasoning,
            SafetyIdentifier = request.SafetyIdentifier,
            ServiceTier = request.ServiceTier,
            Status = initialStatus,
            Store = request.Store,
            Temperature = request.Temperature,
            Text = request.Text,
            ToolChoice = request.ToolChoice,
            Tools = [.. request.Tools ?? []],
            TopLogprobs = request.TopLogprobs,
            TopP = request.TopP,
            Truncation = request.Truncation,
            Usage = ResponseUsage.Zero,
#pragma warning disable CS0618 // Type or member is obsolete
            User = request.User
#pragma warning restore CS0618 // Type or member is obsolete
        };

        ResponseState state = new(request);

        // Store the initial response
        StorageResult<Response> storeResult = await this._storage.StoreResponseAsync(response, cancellationToken).ConfigureAwait(false);
        state.CurrentETag = storeResult.ETag;

        this._runningResponses[responseId] = state;

        return state;
    }

    private async Task ExecuteResponseAsync(string responseId, ResponseState state, CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        // Link the state's cancellation token with the provided cancellation token
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, state.CancellationTokenSource.Token);
        CancellationToken effectiveCt = linkedCts.Token;

        try
        {
            // Create agent invocation context
            AgentInvocationContext context = new(new IdGenerator(responseId: responseId, conversationId: state.Request.Conversation?.Id));

            // Execute using the injected executor
            await foreach (StreamingResponseEvent streamingEvent in this._executor.ExecuteAsync(context, state.Request, effectiveCt).ConfigureAwait(false))
            {
                state.AddStreamingEvent(streamingEvent);

                // Update the stored response
                if (streamingEvent is IStreamingResponseEventWithResponse responseEvent)
                {
                    await this.UpdateStoredResponseAsync(responseEvent.Response, state).ConfigureAwait(false);
                }
            }

            // Update response status to completed if not already in a terminal state
            if (!state.IsTerminal)
            {
                await this.UpdateResponseStatusAsync(responseId, state, ResponseStatus.Completed, null).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Update response status to cancelled
            await this.UpdateResponseStatusAsync(responseId, state, ResponseStatus.Cancelled, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Update response status to failed
            ResponseError error = new()
            {
                Code = "execution_error",
                Message = ex.Message
            };
            await this.UpdateResponseStatusAsync(responseId, state, ResponseStatus.Failed, error).ConfigureAwait(false);
        }
        finally
        {
            // Mark completion time for cleanup
            state.CompletedAt = DateTimeOffset.UtcNow;

            // Signal one final time to unblock any waiting consumers
            state.SignalUpdate();
        }
    }

    private async Task UpdateStoredResponseAsync(Response response, ResponseState state)
    {
        // Update output items from state
        List<ItemResource> outputItems = state.GetOutputItems();
        Response updatedResponse = response with { Output = outputItems };

        StorageResult<Response>? result = await this._storage.UpdateResponseAsync(updatedResponse, state.CurrentETag!, CancellationToken.None).ConfigureAwait(false);
        if (result is not null)
        {
            state.CurrentETag = result.ETag;
        }
    }

    private async Task UpdateResponseStatusAsync(string responseId, ResponseState state, ResponseStatus status, ResponseError? error)
    {
        StorageResult<Response>? currentResult = await this._storage.GetResponseAsync(responseId, CancellationToken.None).ConfigureAwait(false);
        if (currentResult is null)
        {
            return;
        }

        Response updatedResponse = currentResult.Value with
        {
            Status = status,
            Error = error,
            Output = state.GetOutputItems()
        };

        StorageResult<Response>? result = await this._storage.UpdateResponseAsync(updatedResponse, state.CurrentETag!, CancellationToken.None).ConfigureAwait(false);
        if (result is not null)
        {
            state.CurrentETag = result.ETag;
            state.IsTerminal = updatedResponse.IsTerminal;

            // Add a terminal event
            int sequenceNumber = state.StreamingUpdates.Count + 1;
            StreamingResponseEvent terminalEvent = status switch
            {
                ResponseStatus.Completed => new StreamingResponseCompleted
                {
                    SequenceNumber = sequenceNumber,
                    Response = updatedResponse
                },
                ResponseStatus.Cancelled => new StreamingResponseCancelled
                {
                    SequenceNumber = sequenceNumber,
                    Response = updatedResponse
                },
                ResponseStatus.Failed => new StreamingResponseFailed
                {
                    SequenceNumber = sequenceNumber,
                    Response = updatedResponse
                },
                _ => throw new InvalidOperationException($"Unexpected terminal status: {status}")
            };

            state.AddStreamingEvent(terminalEvent);
        }
    }

    private static List<ItemResource> GetInputItems(string responseId, ResponseState state)
    {
        // Use a deterministic random seed. We add 1 to avoid clashing with the output message ids.
        int randomSeed = responseId.GetHashCode() + 1;
        IdGenerator idGenerator = new(responseId: responseId, conversationId: state.Request.Conversation?.Id, randomSeed: randomSeed);
        List<ItemResource> itemResources = [];
        foreach (InputMessage inputMessage in state.Request.Input.GetInputMessages())
        {
            itemResources.AddRange(inputMessage.ToItemResource(idGenerator));
        }

        return itemResources;
    }

    private void CleanupCompletedResponses(object? state)
    {
        if (this._disposed)
        {
            return;
        }

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<string> responsesToRemove = [];

            foreach (KeyValuePair<string, ResponseState> kvp in this._runningResponses)
            {
                // Only clean up responses that have completed and exceeded the retention period
                if (kvp.Value.CompletedAt.HasValue &&
                    now - kvp.Value.CompletedAt.Value >= this._completedResponseRetentionPeriod)
                {
                    responsesToRemove.Add(kvp.Key);
                }
            }

            foreach (string responseId in responsesToRemove)
            {
                if (this._runningResponses.TryRemove(responseId, out ResponseState? removedState))
                {
                    removedState.CancellationTokenSource.Dispose();
                    this._logger.LogDebug("Removed completed response '{ResponseId}' after retention period", responseId);

                    // Also remove from storage (fire and forget)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await this._storage.DeleteResponseAsync(responseId, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            this._logger.LogWarning(ex, "Failed to delete response '{ResponseId}' from storage during cleanup", responseId);
                        }
                    });
                }
            }

            if (responsesToRemove.Count > 0)
            {
                this._logger.LogInformation("Cleaned up {Count} completed responses", responsesToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error during cleanup of completed responses");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this._cleanupTimer.Dispose();
    }
}
