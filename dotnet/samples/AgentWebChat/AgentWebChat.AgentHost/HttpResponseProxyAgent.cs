// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost;

/// <summary>
/// A reusable AIAgent implementation that proxies calls to remote agents via Gateway's /v1/responses endpoint.
/// This enables .NET workflows to call agents running in other languages/runtimes (e.g., Python) through the Gateway.
/// </summary>
public sealed class HttpResponseProxyAgent : AIAgent
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _agentName;
    private readonly string? _description;

    /// <summary>
    /// Simple thread implementation for HttpResponseProxyAgent.
    /// </summary>
    private sealed class ProxyAgentThread : AgentThread
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpResponseProxyAgent"/> class.
    /// </summary>
    /// <param name="httpClient">HttpClient configured to point at the Gateway base address.</param>
    /// <param name="agentName">The name of the remote agent to route to (used for both routing and as the local agent name).</param>
    /// <param name="description">Optional description of the agent.</param>
    public HttpResponseProxyAgent(
        HttpClient httpClient,
        string agentName,
        string? description = null)
    {
        this._httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this._agentName = !string.IsNullOrWhiteSpace(agentName)
            ? agentName
            : throw new ArgumentException("Agent name cannot be null or whitespace.", nameof(agentName));
        this._description = description;
    }

    /// <inheritdoc/>
    public override string Name => this._agentName;

    /// <inheritdoc/>
    public override string? Description => this._description;

    /// <inheritdoc/>
    public override AgentThread GetNewThread(IAgentFeatureCollection? featureCollection = null) => new ProxyAgentThread();

    /// <inheritdoc/>
    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null, IAgentFeatureCollection? featureCollection = null)
    {
        // Basic thread deserialization - can be enhanced if needed
        return new ProxyAgentThread();
    }

    /// <inheritdoc/>
    public override Task<AgentRunResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return this.RunStreamingAsync(messages, thread, options, cancellationToken)
            .ToAgentRunResponseAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Build input messages using factory method
        var inputMessages = messages.Select(m => new InputMessage
        {
            Role = m.Role,
            Content = ConvertMessageContent(m)
        }).ToList();

        // Build the CreateResponse request
        var createRequest = new CreateResponse
        {
            Input = ResponseInput.FromMessages(inputMessages),
            Agent = new AgentReference { Name = this._agentName },
            Stream = true
        };

        // Serialize the request
        var json = JsonSerializer.Serialize(createRequest, s_jsonOptions);

        // Send POST request to /v1/responses
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await this._httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to call Gateway for agent '{this._agentName}': {ex.Message}", ex);
        }

        // Read and parse the SSE stream
        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            // SSE format: "data: {json}\n\n"
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line.Substring(6); // Skip "data: "

                // Parse as dynamic JSON to extract event type and properties
                JsonDocument? jsonDoc = null;
                AgentRunResponseUpdate? update = null;
                try
                {
                    jsonDoc = JsonDocument.Parse(data);
                    var root = jsonDoc.RootElement;

                    // Extract event type
                    if (root.TryGetProperty("type", out var typeElement))
                    {
                        var eventType = typeElement.GetString();
                        // Convert based on event type
                        update = this.ConvertToAgentRunResponseUpdate(eventType, root);
                    }
                }
                catch (Exception ex)
                {
                    // Log and skip malformed events
                    Console.WriteLine($"Failed to deserialize streaming event: {ex.Message}");
                }
                finally
                {
                    jsonDoc?.Dispose();
                }

                if (update is not null)
                {
                    yield return update;
                }
            }
        }
    }

    private static InputMessageContent ConvertMessageContent(ChatMessage message)
    {
        // Convert message contents to text
        var textParts = message.Contents
            .OfType<TextContent>()
            .Select(t => t.Text);

        var combinedText = string.Join(" ", textParts);
        return InputMessageContent.FromText(combinedText);
    }

    private AgentRunResponseUpdate? ConvertToAgentRunResponseUpdate(string? eventType, JsonElement root)
    {
        // Map different event types to AgentRunResponseUpdate
        switch (eventType)
        {
            case "response.output_text.delta":
                if (root.TryGetProperty("delta", out var deltaElement))
                {
                    var delta = deltaElement.GetString();
                    if (!string.IsNullOrEmpty(delta))
                    {
                        return new AgentRunResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = [new TextContent(delta)],
                            AuthorName = this._agentName
                        };
                    }
                }

                break;

            case "response.output_text.done":
                if (root.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        return new AgentRunResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = [new TextContent(text)],
                            AuthorName = this._agentName
                        };
                    }
                }

                break;

            case "response.completed":
                // Final event - no content to yield
                return null;

            case "response.failed":
                if (root.TryGetProperty("error", out var errorElement))
                {
                    var errorJson = errorElement.GetRawText();
                    throw new InvalidOperationException($"Remote agent '{this._agentName}' failed: {errorJson}");
                }

                throw new InvalidOperationException($"Remote agent '{this._agentName}' failed with unknown error");
        }

        // Other event types we don't handle yet
        return null;
    }
}
