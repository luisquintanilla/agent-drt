// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// An in-memory implementation of <see cref="IChatMessagePersistence"/> for development and testing purposes.
/// </summary>
/// <remarks>
/// This implementation stores messages in memory and is not suitable for production use.
/// For production scenarios, implement <see cref="IChatMessagePersistence"/> with a durable storage backend
/// such as a database, blob storage, or distributed cache.
/// </remarks>
public sealed class InMemoryChatMessagePersistence : IChatMessagePersistence
{
    private readonly ConcurrentDictionary<string, (List<ChatMessage> Messages, string ETag)> _storage = new();

    /// <inheritdoc/>
    public Task<(IList<ChatMessage> Messages, string? ETag)> FetchMessagesAsync(string persistenceKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceKey);

        if (this._storage.TryGetValue(persistenceKey, out (List<ChatMessage> Messages, string ETag) entry))
        {
            // Return a copy to prevent external modification
            IList<ChatMessage> messages = new List<ChatMessage>(entry.Messages);
            return Task.FromResult<(IList<ChatMessage>, string?)>((messages, entry.ETag));
        }

        return Task.FromResult<(IList<ChatMessage>, string?)>((Array.Empty<ChatMessage>(), null));
    }

    /// <inheritdoc/>
    public Task<string> AppendMessagesAsync(string persistenceKey, IEnumerable<ChatMessage> messages, string? eTag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceKey);
        ArgumentNullException.ThrowIfNull(messages);

        // Generate new ETag
        string newETag = Guid.NewGuid().ToString("N");

        // Store a copy to prevent external modification
        List<ChatMessage> messagesCopy = new(messages);

        // Perform optimistic concurrency check and update
        bool updated = false;
        while (!updated)
        {
            if (this._storage.TryGetValue(persistenceKey, out (List<ChatMessage> Messages, string ETag) existing))
            {
                // Verify ETag matches
                if (existing.ETag != eTag)
                {
                    throw new InvalidOperationException($"ETag mismatch for key '{persistenceKey}'. Expected '{eTag}', but found '{existing.ETag}'.");
                }

                // Try to update with new ETag
                updated = this._storage.TryUpdate(persistenceKey, (messagesCopy, newETag), existing);
            }
            else
            {
                // First write - eTag should be null
                if (eTag is not null)
                {
                    throw new InvalidOperationException($"ETag mismatch for key '{persistenceKey}'. Expected null for new conversation, but received '{eTag}'.");
                }

                // Try to add new entry
                updated = this._storage.TryAdd(persistenceKey, (messagesCopy, newETag));
            }
        }

        return Task.FromResult(newETag);
    }

    /// <summary>
    /// Clears all persisted messages.
    /// </summary>
    public void Clear()
    {
        this._storage.Clear();
    }

    /// <summary>
    /// Removes persisted messages for the specified key.
    /// </summary>
    /// <param name="persistenceKey">The key identifying the conversation to remove.</param>
    /// <returns><see langword="true"/> if the messages were removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string persistenceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceKey);
        return this._storage.TryRemove(persistenceKey, out _);
    }
}
