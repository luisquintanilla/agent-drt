// Copyright (c) Microsoft. All rights reserved.

#if AGENTGATEWAY
namespace AgentGateway.Responses.Streaming;
#else
namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses.Streaming;
#endif

/// <summary>
/// Implements a sequence number generator.
/// </summary>
public sealed class SequenceNumber
{
    private int _sequenceNumber;

    /// <summary>
    /// Gets the next sequence number.
    /// </summary>
    /// <returns>The next sequence number.</returns>
    public int Increment() => this._sequenceNumber++;
}
