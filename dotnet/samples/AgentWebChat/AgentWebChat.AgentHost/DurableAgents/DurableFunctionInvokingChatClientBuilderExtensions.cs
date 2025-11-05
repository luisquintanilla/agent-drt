// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// Provides extension methods for attaching a <see cref="DurableFunctionInvokingChatClient"/> to a chat pipeline.
/// </summary>
public static class DurableFunctionInvokingChatClientBuilderExtensions
{
    /// <summary>
    /// Enables automatic function call invocation on the chat pipeline.
    /// </summary>
    /// <remarks>This works by adding an instance of <see cref="DurableFunctionInvokingChatClient"/> with default options.</remarks>
    /// <param name="builder">The <see cref="ChatClientBuilder"/> being used to build the chat pipeline.</param>
    /// <param name="loggerFactory">An optional <see cref="ILoggerFactory"/> to use to create a logger for logging function invocations.</param>
    /// <param name="messagePersistence">An optional <see cref="IChatMessagePersistence"/> for persisting chat messages.</param>
    /// <param name="memoStorage">An optional <see cref="IMemoStorage"/> for durable key-value storage with ETag-based concurrency control.</param>
    /// <param name="configure">An optional callback that can be used to configure the <see cref="DurableFunctionInvokingChatClient"/> instance.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ChatClientBuilder UseDurableFunctionInvocation(
        this ChatClientBuilder builder,
        ILoggerFactory? loggerFactory = null,
        IChatMessagePersistence? messagePersistence = null,
        IMemoStorage? memoStorage = null,
        Action<DurableFunctionInvokingChatClient>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerClient, services) =>
        {
            loggerFactory ??= services.GetService<ILoggerFactory>();
            messagePersistence ??= services.GetService<IChatMessagePersistence>();
            memoStorage ??= services.GetService<IMemoStorage>();

            var chatClient = new DurableFunctionInvokingChatClient(
                innerClient,
                loggerFactory?.CreateLogger(typeof(DurableFunctionInvokingChatClient)),
                messagePersistence,
                memoStorage);
            configure?.Invoke(chatClient);
            return chatClient;
        });
    }
}
