// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost;

/// <summary>
/// Base class for agents that support durable conversation management with persistent message storage.
/// </summary>
public abstract class DurableAgent : AIAgent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableAgent"/> class.
    /// </summary>
    protected DurableAgent()
    {
    }

    /// <inheritdoc/>
    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null, IAgentFeatureCollection? featureCollection = null)
        => this.GetNewThread(featureCollection);

    /// <inheritdoc/>
    public override DurableAgentThread GetNewThread(IAgentFeatureCollection? featureCollection = null)
    {
        ArgumentNullException.ThrowIfNull(featureCollection);
        return new DurableAgentThread(featureCollection.Get<ChatMessageStore>()!);
    }

    /// <summary>
    /// Gets all messages stored in the thread.
    /// </summary>
    /// <param name="thread">The agent thread.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The collection of stored messages.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="thread"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="thread"/> is not a <see cref="DurableAgentThread"/> instance.</exception>
    public static async Task<IEnumerable<ChatMessage>> GetMessagesAsync(AgentThread thread, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (thread is not DurableAgentThread durableThread)
        {
            throw new ArgumentException($"Thread must be a {nameof(DurableAgentThread)} instance created by {nameof(DurableAgent)}.", nameof(thread));
        }

        return await durableThread.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A thread type for durable agents that supports persistent message storage.
    /// </summary>
    public sealed class DurableAgentThread : AgentThread
    {
        private readonly ChatMessageStore _messageStore;

        internal DurableAgentThread(ChatMessageStore messageStoreFactory)
        {
            this._messageStore = messageStoreFactory;
        }

        /// <summary>
        /// Adds messages to the thread's message store.
        /// </summary>
        /// <param name="newMessages">The new messages to add.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
        internal async Task MessagesReceivedAsync(IEnumerable<ChatMessage> newMessages, CancellationToken cancellationToken = default)
        {
            // Add messages to the store
            await this._messageStore.AddMessagesAsync(newMessages, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the messages stored in the thread's message store.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
        /// <returns>The collection of stored messages, or an empty collection if no store exists.</returns>
        internal async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
        {
            if (this._messageStore is null)
            {
                return [];
            }

            return await this._messageStore.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null) => default;
    }
}
