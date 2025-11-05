// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// Extension methods for <see cref="ChatOptions"/> to support chat message persistence.
/// </summary>
public static class ChatOptionsExtensions
{
    private const string PersistenceKeyProperty = "PersistenceKey";

    /// <summary>
    /// Gets the persistence key from the <see cref="ChatOptions"/>.
    /// </summary>
    /// <param name="options">The chat options.</param>
    /// <returns>The persistence key if set; otherwise, <see langword="null"/>.</returns>
    public static string? GetPersistenceKey(this ChatOptions? options)
    {
        if (options?.AdditionalProperties is not null &&
            options.AdditionalProperties.TryGetValue(PersistenceKeyProperty, out object? value) &&
            value is string key)
        {
            return key;
        }

        return null;
    }

    /// <summary>
    /// Sets the persistence key on the <see cref="ChatOptions"/>.
    /// </summary>
    /// <param name="options">The chat options.</param>
    /// <param name="persistenceKey">The persistence key to set.</param>
    public static void SetPersistenceKey(this ChatOptions options, string? persistenceKey)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (persistenceKey is not null)
        {
            options.AdditionalProperties ??= [];
            options.AdditionalProperties[PersistenceKeyProperty] = persistenceKey;
        }
        else
        {
            options.AdditionalProperties?.Remove(PersistenceKeyProperty);
        }
    }
}
