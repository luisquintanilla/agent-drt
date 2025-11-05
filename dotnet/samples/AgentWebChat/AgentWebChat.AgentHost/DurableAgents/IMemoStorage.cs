// Copyright (c) Microsoft. All rights reserved.

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>
/// Interface for durable key-value storage with ETag-based concurrency control.
/// </summary>
public interface IMemoStorage
{
    /// <summary>
    /// Retrieves the memo for the specified key.
    /// </summary>
    /// <param name="key">The key identifying the memo to retrieve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Memo"/> containing the key-value pairs and ETag. Returns an empty memo with null ETag if no data exists.</returns>
    Task<Memo> GetMemoAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the memo for the specified key with optimistic concurrency control.
    /// </summary>
    /// <param name="key">The key identifying the memo to set.</param>
    /// <param name="memo">The memo to store, including its ETag for concurrency control.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The updated memo with the new ETag.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the ETag doesn't match, indicating a concurrency conflict.</exception>
    Task<Memo> SetMemoAsync(string key, Memo memo, CancellationToken cancellationToken = default);
}
