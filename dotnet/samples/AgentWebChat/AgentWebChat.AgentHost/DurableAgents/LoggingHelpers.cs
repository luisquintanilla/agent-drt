// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>Provides internal helpers for implementing logging.</summary>
internal static class LoggingHelpers
{
    /// <summary>Serializes <paramref name="value"/> as JSON for logging purposes.</summary>
    public static string AsJson<T>(T value, JsonSerializerOptions? options)
    {
        if (options?.TryGetTypeInfo(typeof(T), out var typeInfo) is true ||
            AIJsonUtilities.DefaultOptions.TryGetTypeInfo(typeof(T), out typeInfo))
        {
            try
            {
                return JsonSerializer.Serialize(value, typeInfo);
            }
            catch
            {
            }
        }

        // If we're unable to get a type info for the value, or if we fail to serialize,
        // return an empty JSON object. We do not want lack of type info to disrupt application behavior with exceptions.
        return "{}";
    }
}
