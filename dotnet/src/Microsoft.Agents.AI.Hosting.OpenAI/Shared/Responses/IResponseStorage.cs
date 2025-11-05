// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

#if AGENTGATEWAY
using AgentGateway.Responses.Models;

namespace AgentGateway.Responses;
#else
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;
#endif

/// <summary>
/// Represents the result of a storage operation with ETag support.
/// </summary>
/// <typeparam name="T">The type of the stored value.</typeparam>
internal sealed record StorageResult<T>
{
    /// <summary>
    /// Gets the value stored in the result.
    /// </summary>
    public required T Value { get; init; }

    /// <summary>
    /// Gets the ETag for the stored value, used for optimistic concurrency control.
    /// </summary>
    public required string ETag { get; init; }
}

/// <summary>
/// Storage abstraction for response state and metadata.
/// Implementations should provide thread-safe storage with ETag-based optimistic concurrency control.
/// </summary>
internal interface IResponseStorage
{
    /// <summary>
    /// Stores a response with initial ETag.
    /// </summary>
    /// <param name="response">The response to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A storage result containing the stored response and its ETag.</returns>
    Task<StorageResult<Response>> StoreResponseAsync(
        Response response,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a response by ID.
    /// </summary>
    /// <param name="responseId">The ID of the response to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A storage result containing the response and its ETag if found, null otherwise.</returns>
    Task<StorageResult<Response>?> GetResponseAsync(
        string responseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a response using optimistic concurrency control.
    /// </summary>
    /// <param name="response">The updated response to store.</param>
    /// <param name="expectedETag">The expected ETag for concurrency control. If the current ETag doesn't match, the update fails.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A storage result containing the updated response and new ETag if successful, null if the ETag didn't match.</returns>
    Task<StorageResult<Response>?> UpdateResponseAsync(
        Response response,
        string expectedETag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a response by ID.
    /// </summary>
    /// <param name="responseId">The ID of the response to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the response was deleted, false if it was not found.</returns>
    Task<bool> DeleteResponseAsync(
        string responseId,
        CancellationToken cancellationToken = default);
}
