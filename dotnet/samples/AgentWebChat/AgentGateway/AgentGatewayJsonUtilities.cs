// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentGateway.Conversations;
using AgentGateway.Responses;
using AgentGateway.Workflows;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.DevUI.Entities;

namespace AgentGateway;

/// <summary>
/// Provides JSON serialization options and context for AgentGateway to support AOT and trimming.
/// Includes Orleans grain state types and chains to OpenAI Hosting type resolvers.
/// </summary>
public static class AgentGatewayJsonUtilities
{
    /// <summary>
    /// Gets the default <see cref="JsonSerializerOptions"/> instance used for AgentGateway serialization.
    /// Includes support for grain state types and chains to OpenAIJsonUtilities for complete type coverage.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Type info resolver chaining is AOT-safe.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "Type info resolver chaining is AOT-safe.")]
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        // Start with our source-generated context for grain states
        JsonSerializerOptions options = new(AgentGatewayJsonContext.Default.Options);

        // Chain with OpenAI Hosting types (which includes Microsoft.Extensions.AI types)
        options.TypeInfoResolverChain.Add(OpenAIHostingJsonUtilities.DefaultOptions.TypeInfoResolver!);

        options.MakeReadOnly();
        return options;
    }
}

/// <summary>
/// Provides a JSON serialization context for AgentGateway to support AOT and trimming.
/// Includes API types and grain state types. The grain state types and their contained types
/// are also handled by the chained OpenAIJsonUtilities.DefaultOptions.TypeInfoResolver.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(ConversationState))]
[JsonSerializable(typeof(ResponseState))]
[JsonSerializable(typeof(AgentConversationIndexState))]
[JsonSerializable(typeof(DiscoveryResponse))]
[JsonSerializable(typeof(List<EntityInfo>))]
[JsonSerializable(typeof(WorkflowGrainState))]
[JsonSerializable(typeof(ETagResponse))]
[ExcludeFromCodeCoverage]
internal sealed partial class AgentGatewayJsonContext : JsonSerializerContext;
