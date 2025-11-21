// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;

/// <summary>
/// Configuration options for the responses service.
/// </summary>
public sealed class ResponsesServiceOptions
{
    /// <summary>
    /// Gets or sets the retention period for completed responses.
    /// Completed responses will be automatically removed after this timespan.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan CompletedResponseRetentionPeriod { get; set; } = TimeSpan.FromMinutes(5);
}
