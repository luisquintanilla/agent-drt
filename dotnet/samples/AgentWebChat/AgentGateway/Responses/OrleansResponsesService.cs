// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentGateway.Models;
using AgentGateway.Responses.Models;
using Orleans;

namespace AgentGateway.Responses;

/// <summary>
/// Orleans-backed implementation of IResponsesService for handling OpenAI Responses API operations.
/// This implementation uses Orleans grains to provide distributed, scalable response management.
/// </summary>
internal sealed class OrleansResponsesService : IResponsesService
{
    private readonly IGrainFactory _grainFactory;

    public OrleansResponsesService(IGrainFactory grainFactory)
    {
        ArgumentNullException.ThrowIfNull(grainFactory);
        this._grainFactory = grainFactory;
    }

    public ValueTask<ResponseError?> ValidateRequestAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<ResponseError?>(null);

    /// <summary>
    /// Creates a model response for the given input using ChatClientAgent.
    /// </summary>
    public async Task<Response> CreateResponseAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var responseId = $"resp_{Guid.NewGuid():N}";
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);
        return await grain.CreateAsync(request, cancellationToken);
    }

    /// <summary>
    /// Creates a streaming model response for the given input using ChatClientAgent.
    /// </summary>
    public async IAsyncEnumerable<StreamingResponseEvent> CreateResponseStreamingAsync(
        CreateResponse request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var responseId = $"resp_{Guid.NewGuid():N}";
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);

        await foreach (var update in grain.CreateStreamingAsync(request, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Retrieves a response by ID.
    /// </summary>
    public async Task<Response?> GetResponseAsync(string responseId, CancellationToken cancellationToken = default)
    {
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);
        return await grain.GetAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves a response by ID in streaming mode, yielding events as they become available.
    /// </summary>
    /// <param name="responseId">The ID of the response to retrieve.</param>
    /// <param name="startingAfter">The sequence number after which to start streaming. If null, starts from the beginning.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of streaming updates.</returns>
    public async IAsyncEnumerable<StreamingResponseEvent> GetResponseStreamingAsync(
        string responseId,
        int? startingAfter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);

        await foreach (var update in grain.GetStreamingAsync(startingAfter, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Deletes a response by ID.
    /// </summary>
    public Task<bool> DeleteResponseAsync(
        string responseId,
        CancellationToken cancellationToken = default)
    {
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);
        return grain.DeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Lists the input items for a response.
    /// </summary>
    public async Task<ListResponse<ItemResource>> ListResponseInputItemsAsync(
        string responseId,
        int? limit,
        SortOrder? order,
        string? after,
        string? before,
        CancellationToken cancellationToken = default)
    {
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);
        return await grain.ListInputItemsAsync(limit, order, after, before, cancellationToken);
    }

    /// <summary>
    /// Cancels an in-progress response.
    /// Only responses created with background=true can be cancelled.
    /// </summary>
    public async Task<Response> CancelResponseAsync(
        string responseId,
        CancellationToken cancellationToken = default)
    {
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);
        return await grain.CancelAsync(cancellationToken);
    }
}
