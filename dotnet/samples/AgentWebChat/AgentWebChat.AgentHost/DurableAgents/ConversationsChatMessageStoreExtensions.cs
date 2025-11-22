// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AgentHost.DurableAgents.Utilities;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Conversations API-backed conversation storage.
/// </summary>
public static class ConversationsChatMessageStoreExtensions
{
    /// <summary>
    /// Registers the Conversations API-backed conversation storage implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This extension method registers <see cref="ConversationsChatMessageStore"/> as the implementation
    /// of <see cref="IConversationStorage"/>. This enables conversation and message storage using the
    /// OpenAI Conversations API exposed by the AgentGateway.
    /// </para>
    /// <para>
    /// Requires that <see cref="ConversationsApiClient"/> is already registered in the service collection,
    /// typically via AddHttpClient.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// builder.Services.AddHttpClient&lt;ConversationsApiClient&gt;(client => client.BaseAddress = new Uri(gatewayAddress));
    /// builder.Services.AddConversationsChatMessageStore();
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddConversationStorageClient(this IServiceCollection services)
    {
        services.AddSingleton<IConversationStorage, ConversationsChatMessageStore>();
        return services;
    }
}
