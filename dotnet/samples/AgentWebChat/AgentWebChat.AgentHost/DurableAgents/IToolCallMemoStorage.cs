// Copyright (c) Microsoft. All rights reserved.

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>
/// Interface for memo storage scoped to a specific tool call.
/// </summary>
public interface IToolCallMemoStorage
{
    /// <summary>
    /// Retrieves the memo for the current tool call.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Memo"/> containing the key-value pairs and ETag. Returns an empty memo with null ETag if no data exists.</returns>
    Task<Memo> GetMemoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the memo for the current tool call with optimistic concurrency control.
    /// </summary>
    /// <param name="memo">The memo to store, including its ETag for concurrency control.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The updated memo with the new ETag.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the ETag doesn't match, indicating a concurrency conflict.</exception>
    Task<Memo> SetMemoAsync(Memo memo, CancellationToken cancellationToken = default);
}
