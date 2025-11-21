// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

#if AGENTGATEWAY
namespace AgentGateway.Conversations.Models;
#else
namespace Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
#endif

/// <summary>
/// Request to update an existing conversation.
/// </summary>
public sealed class UpdateConversationRequest
{
    /// <summary>
    /// Set of 16 key-value pairs that can be attached to a conversation.
    /// </summary>
    [JsonPropertyName("metadata")]
    public required Dictionary<string, string> Metadata { get; init; }
}
