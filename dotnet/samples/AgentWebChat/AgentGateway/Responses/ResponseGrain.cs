// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentGateway.Conversations;
using AgentGateway.Utilities;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace AgentGateway.Responses;

/// <summary>
/// State for a response grain, containing the response metadata.
/// </summary>
[GenerateSerializer]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class ResponseState
{
    /// <summary>
    /// The response metadata.
    /// </summary>
    [Id(0)]
    public Response? Response { get; set; }

    /// <summary>
    /// The original request, stored for background execution and debugging.
    /// </summary>
    [Id(2)]
    public CreateResponse? Request { get; set; }

    /// <summary>
    /// The streaming updates collected during response generation.
    /// </summary>
    [Id(3)]
    public List<StreamingResponseEvent> StreamingUpdates { get; set; } = [];

    /// <summary>
    /// The last message ID in the conversation before execution started.
    /// Used for idempotent message appending.
    /// </summary>
    [Id(4)]
    public string? LastMessageIdBeforeExecution { get; set; }
}

/// <summary>
/// Grain interface for managing a single response.
/// </summary>
internal interface IResponseGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates a new response and generates it using the ChatClientAgent.
    /// </summary>
    /// <param name="request">The request to create the response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created response.</returns>
    Task<Response> CreateAsync(CreateResponse request, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a streaming response using the ChatClientAgent.
    /// </summary>
    /// <param name="request">The request to create the response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of streaming updates.</returns>
    IAsyncEnumerable<StreamingResponseEvent> CreateStreamingAsync(CreateResponse request, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the response.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response if it exists, null otherwise.</returns>
    Task<Response?> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the response in streaming mode, yielding events as they become available.
    /// </summary>
    /// <param name="startingAfter">The sequence number after which to start streaming. If null, starts from the beginning.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of streaming updates.</returns>
    IAsyncEnumerable<StreamingResponseEvent> GetStreamingAsync(int? startingAfter, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the input items for this response.
    /// </summary>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="order">Sort order.</param>
    /// <param name="after">Return items after this ID.</param>
    /// <param name="before">Return items before this ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list response with items and pagination info.</returns>
    Task<ListResponse<ItemResource>> ListInputItemsAsync(int? limit, SortOrder? order, string? after, string? before, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the response and its full thread (input + output messages), waiting for completion if necessary.
    /// This is more efficient than calling GetAsync() followed by ListInputItemsAsync() when you need both.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the response and the full list of items (input + output), or null if the response doesn't exist.</returns>
    Task<(Response Response, List<ItemResource> Items)?> GetWithThreadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Cancels an in-progress response.
    /// Only responses created with background=true can be cancelled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated response after cancellation.</returns>
    Task<Response> CancelAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the response and all its data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the response was deleted, false if it was not found.</returns>
    Task<bool> DeleteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Orleans grain implementation for managing a response.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class ResponseGrain(
    [PersistentState("state")] IPersistentState<ResponseState> responseState,
    IResponseExecutor responseExecutor,
    ILogger<ResponseGrain> logger) : Grain, IResponseGrain, IRemindable, IDisposable
{
    private const int DefaultListInputItemLimit = 20;
    private const string BackgroundExecutionReminderName = "BackgroundExecution";
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly CancellationTokenSource _executionCts = new();
    private readonly AsyncManualResetEvent _streamingUpdatedEvent = new();
    private Task? _executionTask;

    private string ResponseId => this.GetPrimaryKeyString();

    private string? ConversationId => responseState.State.Response?.Conversation?.Id;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // If we have a pending request that's not completed, ensure reminder is registered and start execution
        if (responseState.State.Request is not null &&
            responseState.State.Response is { IsTerminal: false })
        {
            // Ensure reminder is registered for background execution
            await this.RegisterOrUpdateReminder(
                BackgroundExecutionReminderName,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));
            this._executionTask = this.RunAsync(this._executionCts.Token);
        }
    }
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await this._shutdownCts.CancelAsync();
        this._executionCts.Cancel();
        this._streamingUpdatedEvent.Cancel();

        if (this._executionTask is not null)
        {
            try
            {
                await this._executionTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }
    }

    public async Task<Response> CreateAsync(CreateResponse request, CancellationToken cancellationToken)
    {
        if (responseState.State.Response is not null)
        {
            throw new InvalidOperationException($"Response with ID '{this.ResponseId}' already exists.");
        }

        // Validate mutual exclusivity of conversation.id and previous_response_id
        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id) &&
            !string.IsNullOrEmpty(request.PreviousResponseId))
        {
            throw new InvalidOperationException("Mutually exclusive parameters: 'conversation' and 'previous_response_id'. Ensure you are only providing one of: 'previous_response_id' or 'conversation'.");
        }

        // Per OpenAI documentation: "To start response generation in the background, make an API request with background set to true"
        // and "You can create a background Response and start streaming events from it right away... create a Response with both background and stream set to true."
        // See: https://platform.openai.com/docs/guides/background
        if (request.Stream == true)
        {
            throw new InvalidOperationException("Cannot create a streaming response using CreateAsync. Use CreateStreamingAsync instead.");
        }

        // Store the request and create initial response
        await this.InitializeResponseAsync(request, cancellationToken);
        Debug.Assert(responseState.State.Response is not null);
        // Start execution task for both background and non-background requests
        // Background requests will return immediately with queued status
        Debug.Assert(this._executionTask is null);
        this._executionTask = this.RunAsync(this._executionCts.Token);

        // If background execution is requested, return immediately with queued status
        // The execution task will continue running in the background
        if (request.Background == true)
        {
            return responseState.State.Response;
        }

        // For non-background requests, wait for completion by watching for terminal status
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (responseState.State.Response.IsTerminal)
            {
                return responseState.State.Response;
            }

            // Wait for the next update event
            await this._streamingUpdatedEvent.WaitAsync(cancellationToken);
        }
    }

    public async IAsyncEnumerable<StreamingResponseEvent> CreateStreamingAsync(
        CreateResponse request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (responseState.State.Response is not null)
        {
            throw new InvalidOperationException($"Response with ID '{this.ResponseId}' already exists.");
        }

        // Validate mutual exclusivity of conversation.id and previous_response_id
        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id) &&
            !string.IsNullOrEmpty(request.PreviousResponseId))
        {
            throw new InvalidOperationException("Mutually exclusive parameters: 'conversation' and 'previous_response_id'. Ensure you are only providing one of: 'previous_response_id' or 'conversation'.");
        }

        // Per OpenAI documentation: "You can create a background Response and start streaming events from it right away...
        // create a Response with both background and stream set to true."
        // See: https://platform.openai.com/docs/guides/background
        if (request.Stream == false)
        {
            throw new InvalidOperationException("Cannot create a non-streaming response using CreateStreamingAsync. Use CreateAsync instead.");
        }

        // Store the request and create initial response
        await this.InitializeResponseAsync(request, cancellationToken);
        Debug.Assert(responseState.State.Response is not null);

        // Start execution task
        // For background streaming, the task runs in background and events are streamed as they happen
        // For non-background streaming, the task runs and we stream all events until completion
        Debug.Assert(this._executionTask is null);
        this._executionTask = this.RunAsync(this._executionCts.Token);

        // Stream updates as they become available
        var streamedCount = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Yield any new updates
            while (streamedCount < responseState.State.StreamingUpdates.Count)
            {
                yield return responseState.State.StreamingUpdates[streamedCount];
                streamedCount++;
            }

            // Check if we're done
            if (responseState.State.Response?.IsTerminal == true)
            {
                break;
            }

            // Wait for more updates
            await this._streamingUpdatedEvent.WaitAsync(cancellationToken);
        }

        // Wait for execution task to complete to ensure all cleanup is done
        await this._executionTask.WaitAsync(cancellationToken);
    }

    public Task<Response?> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(responseState.State.Response);
    }

    public async IAsyncEnumerable<StreamingResponseEvent> GetStreamingAsync(
        int? startingAfter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (responseState.State.Response is null)
        {
            yield break;
        }

        // Stream existing updates starting from the specified position
        var streamedCount = startingAfter ?? 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Yield any available updates from the current position
            while (streamedCount < responseState.State.StreamingUpdates.Count)
            {
                yield return responseState.State.StreamingUpdates[streamedCount];
                streamedCount++;
            }

            // Check if we're done
            if (responseState.State.Response.IsTerminal)
            {
                break;
            }

            // Wait for more updates
            await this._streamingUpdatedEvent.WaitAsync(cancellationToken);
        }
    }

    public Task<ListResponse<ItemResource>> ListInputItemsAsync(int? limit, SortOrder? order, string? after, string? before, CancellationToken cancellationToken)
    {
        if (responseState.State.Response is null)
        {
            // Return empty list if response doesn't exist yet
            return Task.FromResult(new ListResponse<ItemResource>
            {
                Data = [],
                FirstId = null,
                LastId = null,
                HasMore = false
            });
        }

        var effectiveLimit = Math.Clamp(limit ?? DefaultListInputItemLimit, 1, 100);

        List<ItemResource> itemResources = this.GetInputItems();
        var limitedItems = itemResources.Take(effectiveLimit).ToList();

        return Task.FromResult(new ListResponse<ItemResource>
        {
            Data = limitedItems,
            FirstId = limitedItems.Count > 0 ? limitedItems[0].Id : null,
            LastId = limitedItems.Count > 0 ? limitedItems[^1].Id : null,
            HasMore = itemResources.Count > limit
        });
    }

    private List<ItemResource> GetInputItems()
    {
        // Generate input items (messages) from the stored request
        var itemResources = new List<ItemResource>();
        if (responseState.State.Request is not null)
        {
            // Use a deterministic random seed. We add 1 to avoid clashing with the output message ids, which otherwise use the same seed.
            var randomSeed = (int)unchecked(this.GetGrainId().GetUniformHashCode() + 1);
            var idGenerator = new IdGenerator(responseId: this.ResponseId, conversationId: this.ConversationId, randomSeed: randomSeed);
            foreach (var inputMessage in responseState.State.Request.Input.GetInputMessages())
            {
                itemResources.AddRange(inputMessage.ToItemResource(idGenerator));
            }
        }

        return itemResources;
    }

    public async Task<(Response Response, List<ItemResource> Items)?> GetWithThreadAsync(CancellationToken cancellationToken)
    {
        if (responseState.State.Response is null)
        {
            return null;
        }

        // Wait for completion if not in a terminal state
        while (!responseState.State.Response.IsTerminal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await this._streamingUpdatedEvent.WaitAsync(cancellationToken);
        }

        var response = responseState.State.Response;
        return (response, [.. this.GetInputItems(), .. response.Output]);
    }

    /// <summary>
    /// Gets the last message ID from the conversation or previous response for idempotent message appending.
    /// Per OpenAI API behavior: conversation.id and previous_response_id are mutually exclusive.
    /// The previous_response_id determines the conversation thread context, even if the previous response was created with a conversation.id.
    /// </summary>
    private async Task<string?> GetLastMessageIdAsync(CreateResponse request, CancellationToken cancellationToken)
    {
        string? lastMessageId = null;

        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id))
        {
            var conversationGrain = this.GrainFactory.GetGrain<IConversationGrain>(request.Conversation.Id);

            // Get the last message ID from the conversation
            await foreach (var itemResource in conversationGrain.GetAllItemsAsync(SortOrder.Ascending))
            {
                lastMessageId = itemResource.Id;
            }
        }
        else if (!string.IsNullOrEmpty(request.PreviousResponseId))
        {
            var previousGrain = this.GrainFactory.GetGrain<IResponseGrain>(request.PreviousResponseId);
            var previousResult = await previousGrain.GetWithThreadAsync(cancellationToken);

            if (previousResult is not null)
            {
                var (previousResponse, _) = previousResult.Value;

                // Check if we have a conversation ID to load the last message from
                var conversationId = previousResponse.Conversation?.Id;
                if (conversationId is not null)
                {
                    var conversationGrain = this.GrainFactory.GetGrain<IConversationGrain>(conversationId);

                    // Get the last message ID from the conversation
                    await foreach (var itemResource in conversationGrain.GetAllItemsAsync(SortOrder.Ascending))
                    {
                        lastMessageId = itemResource.Id;
                    }
                }
                else
                {
                    // Track the last message ID from the previous response's output
                    if (previousResponse.Output.Count > 0)
                    {
                        lastMessageId = previousResponse.Output[^1].Id;
                    }
                }
            }
        }

        return lastMessageId;
    }

    /// <summary>
    /// Initializes the response state with the request and creates the initial response object.
    /// </summary>
    /// <param name="request">The create response request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task InitializeResponseAsync(CreateResponse request, CancellationToken cancellationToken)
    {
        var metadata = request.Metadata ?? [];

        // Store conversation ID if provided
        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id))
        {
            metadata["conversationId"] = request.Conversation.Id;
            if (request.Conversation.Metadata is not null)
            {
                foreach (var kvp in request.Conversation.Metadata)
                {
                    metadata[$"conversation.{kvp.Key}"] = kvp.Value;
                }
            }
        }

        // Create initial response
        // Background responses always start as "queued", non-background as "in_progress"
        var initialStatus = request.Background is true ? ResponseStatus.Queued : ResponseStatus.InProgress;
        var response = new Response
        {
            Id = this.ResponseId,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = request.Model ?? "default",
            Status = initialStatus,
            Error = null,
            IncompleteDetails = null,
            Output = [],
            Instructions = request.Instructions,
            Usage = ResponseUsage.Zero,
            ParallelToolCalls = request.ParallelToolCalls ?? true,
            Tools = [],
            ToolChoice = default,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Metadata = metadata,
            Conversation = request.Conversation,
        };

        // Store the request and response
        responseState.State.Request = request;
        responseState.State.Response = response;
        responseState.State.StreamingUpdates.Clear();

        await responseState.WriteStateAsync(cancellationToken);

        await this.RegisterOrUpdateReminder(
            reminderName: BackgroundExecutionReminderName,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Executes the response generation in streaming mode in the background.
    /// This is the single execution path for all modes (blocking, async, streaming).
    /// This method is idempotent and can be safely called multiple times.
    /// </summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Yield immediately so the task can be captured and can run in the background.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext);

        var request = responseState.State.Request;
        Debug.Assert(request is not null);

        var response = responseState.State.Response;
        Debug.Assert(response is not null);

        // Check if already in a terminal state - idempotency check
        try
        {
            if (!response.IsTerminal)
            {
                await this.ProcessRequestAsync(cancellationToken);
            }

            await this.FinalizeResponseAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Update response status to cancelled
            responseState.State.Response = responseState.State.Response! with
            {
                Status = ResponseStatus.Cancelled
            };

            var sequenceNumber = responseState.State.StreamingUpdates.Count + 1;
            var cancelledEvent = new StreamingResponseCancelled
            {
                SequenceNumber = sequenceNumber,
                Response = responseState.State.Response
            };
            responseState.State.StreamingUpdates.Add(cancelledEvent);

            await responseState.WriteStateAsync(CancellationToken.None);
            this._streamingUpdatedEvent.SignalAndReset();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing streaming response {ResponseId}", this.ResponseId);

            // Update response status to failed
            responseState.State.Response = responseState.State.Response! with
            {
                Status = ResponseStatus.Failed,
                Error = new ResponseError
                {
                    Code = "execution_error",
                    Message = ex.Message
                }
            };

            // Emit failed event
            var sequenceNumber = responseState.State.StreamingUpdates.Count + 1;
            var failedEvent = new StreamingResponseFailed
            {
                SequenceNumber = sequenceNumber,
                Response = responseState.State.Response
            };
            responseState.State.StreamingUpdates.Add(failedEvent);

            await responseState.WriteStateAsync(CancellationToken.None);
            this._streamingUpdatedEvent.SignalAndReset();
        }
    }

    private async Task ProcessRequestAsync(CancellationToken cancellationToken)
    {
        var request = responseState.State.Request;
        Debug.Assert(request is not null);

        // Get the last message ID before execution for idempotent appending
        responseState.State.LastMessageIdBeforeExecution = await this.GetLastMessageIdAsync(request, cancellationToken);

        // Create agent invocation context
        // To ensure idempotency, we derive a random seed from the response ID hash code.
        var randomSeed = (int)this.GetGrainId().GetUniformHashCode();
        var context = new AgentInvocationContext(new IdGenerator(responseId: this.ResponseId, conversationId: this.ConversationId, randomSeed: randomSeed));

        // Use the injected response executor to generate the response
        await foreach (var streamingEvent in responseExecutor.ExecuteAsync(
            context,
            request,
            cancellationToken))
        {
            responseState.State.StreamingUpdates.Add(streamingEvent);
            this._streamingUpdatedEvent.SignalAndReset();

            // For each event which updates the underlying Response object, update the current state with the response.
            if (streamingEvent is IStreamingResponseEventWithResponse responseEvent)
            {
                await UpdateResponse(responseEvent.Response, cancellationToken);
            }
        }

        await responseState.WriteStateAsync(cancellationToken);
        this._streamingUpdatedEvent.SignalAndReset();

        async Task UpdateResponse(Response response, CancellationToken cancellationToken)
        {
            responseState.State.Response = response;

            await responseState.WriteStateAsync(cancellationToken);
            this._streamingUpdatedEvent.SignalAndReset();
        }
    }

    private async Task FinalizeResponseAsync(CancellationToken cancellationToken)
    {
        var request = responseState.State.Request;
        Debug.Assert(request is not null);
        var response = responseState.State.Response;
        Debug.Assert(response is not null);

        // Append messages to the conversation if applicable
        if (response.Status == ResponseStatus.Completed && request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id))
        {
            var conversationGrain = this.GrainFactory.GetGrain<IConversationGrain>(request.Conversation.Id);

            // Use the last message ID captured before execution started for idempotency
            var lastMessageIdBeforeExecution = responseState.State.LastMessageIdBeforeExecution;

            // Build the list of messages to append
            var messagesToAppend = new List<ItemResource>();
            messagesToAppend.AddRange(this.GetInputItems());

            // Add output items - they're already ItemResource
            messagesToAppend.AddRange(response.Output);

            var appendedCount = await conversationGrain.AppendItemsAsync(messagesToAppend, lastMessageIdBeforeExecution, cancellationToken);
            if (appendedCount != messagesToAppend.Count)
            {
                if (appendedCount < 0)
                {
                    logger.LogWarning("No messages appended to conversation {ConversationId} for response {ResponseId} due to concurrency conflict",
                        request.Conversation.Id, this.ResponseId);
                }
                else
                {
                    logger.LogWarning("Appended {AppendedCount} out of {TotalCount} messages to conversation {ConversationId} for response {ResponseId}",
                        appendedCount, messagesToAppend.Count, request.Conversation.Id, this.ResponseId);
                }
            }
        }

        var reminder = await this.GetReminder(BackgroundExecutionReminderName);
        if (reminder is not null)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    public async Task<Response> CancelAsync(CancellationToken cancellationToken)
    {
        if (responseState.State.Response is null)
        {
            throw new InvalidOperationException($"Response '{this.ResponseId}' not found.");
        }

        if (responseState.State.Response.Background != true)
        {
            throw new InvalidOperationException($"Only background responses can be cancelled. Response '{this.ResponseId}' was not created with background=true.");
        }

        if (responseState.State.Response.IsTerminal)
        {
            throw new InvalidOperationException($"Response '{this.ResponseId}' is already in a terminal state and cannot be cancelled.");
        }

        // Cancel the execution
        this._executionCts.Cancel();

        // Update response status
        responseState.State.Response = responseState.State.Response with
        {
            Status = ResponseStatus.Cancelled
        };

        // Emit cancelled event
        var sequenceNumber = responseState.State.StreamingUpdates.Count + 1;
        var cancelledEvent = new StreamingResponseCancelled
        {
            SequenceNumber = sequenceNumber,
            Response = responseState.State.Response
        };
        responseState.State.StreamingUpdates.Add(cancelledEvent);

        await responseState.WriteStateAsync(cancellationToken);
        this._streamingUpdatedEvent.SignalAndReset();

        return responseState.State.Response;
    }

    public async Task<bool> DeleteAsync(CancellationToken cancellationToken)
    {
        if (responseState.State.Response is null)
        {
            return false;
        }

        // Cancel any ongoing execution
        this._executionCts.Cancel();

        // Clear the state
        await responseState.ClearStateAsync(cancellationToken);

        return true;
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName == BackgroundExecutionReminderName && this._executionTask?.IsCompleted != false)
        {
            this._executionTask = this.RunAsync(this._executionCts.Token);
        }
    }

    public void Dispose()
    {
        this._executionCts.Dispose();
        this._shutdownCts.Dispose();
    }
}
