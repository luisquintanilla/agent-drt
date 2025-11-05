// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AgentHost.DurableAgents.Utilities;
using Microsoft.Agents.AI;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Conversations API-backed chat message storage.
/// </summary>
public static class ConversationsChatMessageStoreExtensions
{
    /// <summary>
    /// Registers a factory for creating Conversations API-backed chat message stores.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This extension method registers a factory function that creates <see cref="ConversationsChatMessageStore"/>
    /// instances for storing chat messages using the OpenAI Conversations API exposed by the AgentGateway.
    /// </para>
    /// <para>
    /// The factory creates an HttpClient configured to call the Conversations API endpoints and returns
    /// a store instance for the specified conversation ID.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// builder.Services.AddConversationsChatMessageStore();
    ///
    /// // Later, inject the factory:
    /// public MyAgent(Func&lt;string, ChatMessageStore&gt; messageStoreFactory)
    /// {
    ///     var store = messageStoreFactory("my-conversation-id");
    ///     // Use the store...
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddConversationsChatMessageStore(this IServiceCollection services)
    {
        services.AddSingleton<Func<string, ChatMessageStore>>(sp =>
        {
            var apiClient = sp.GetRequiredService<ConversationsApiClient>();
            ChatMessageStore ChatMessageStoreFactory(string conversationId) => new ConversationsChatMessageStore(apiClient, conversationId);
            return ChatMessageStoreFactory;
        });

        return services;
    }
}
