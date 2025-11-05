// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// Interface for persisting and fetching chat messages for durable agents.
/// </summary>
public interface IChatMessagePersistence
{
    /// <summary>
    /// Fetches the persisted chat messages for the specified conversation.
    /// </summary>
    /// <param name="persistenceKey">The key identifying the conversation to fetch messages for.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A tuple containing the list of chat messages and an ETag for concurrency control. Returns an empty list and null ETag if no messages exist.</returns>
    Task<(IList<ChatMessage> Messages, string? ETag)> FetchMessagesAsync(string persistenceKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends chat messages for the specified conversation.
    /// </summary>
    /// <param name="persistenceKey">The key identifying the conversation to append messages for.</param>
    /// <param name="messages">The chat messages to append.</param>
    /// <param name="eTag">The ETag from the last fetch operation for optimistic concurrency control. Pass null for the first write.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The new ETag after the append operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the ETag doesn't match, indicating a concurrency conflict.</exception>
    Task<string> AppendMessagesAsync(string persistenceKey, IEnumerable<ChatMessage> messages, string? eTag, CancellationToken cancellationToken = default);
}
