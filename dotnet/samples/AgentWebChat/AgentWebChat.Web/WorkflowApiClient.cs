// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentContracts;
using AgentContracts.Workflows;

namespace AgentWebChat.Web;

/// <summary>
/// HTTP client for interacting with workflows via the Gateway API.
/// Implements IWorkflowClient for use by the Blazor frontend.
/// </summary>
public sealed class WorkflowApiClient : IWorkflowClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowApiClient"/> class.
    /// </summary>
    public WorkflowApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _jsonOptions = AgentContractsJsonUtilities.DefaultOptions;
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun> StartWorkflowAsync(
        StartWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _httpClient.PostAsJsonAsync(
            "/v1/workflows",
            request,
            _jsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WorkflowRun>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Failed to parse workflow run response");
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun?> GetWorkflowAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}", UriKind.Relative);
        var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WorkflowRun>(_jsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<WorkflowListResponse<WorkflowRunSummary>> ListWorkflowsAsync(
        ListWorkflowsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();

        if (request?.Status.HasValue == true)
        {
            query.Add($"status={request.Status.Value}");
        }

        if (request?.Limit > 0)
        {
            query.Add($"limit={request.Limit}");
        }

        if (!string.IsNullOrEmpty(request?.After))
        {
            query.Add($"after={Uri.EscapeDataString(request.After)}");
        }

        if (!string.IsNullOrEmpty(request?.Before))
        {
            query.Add($"before={Uri.EscapeDataString(request.Before)}");
        }

        var url = query.Count > 0
            ? $"/v1/workflows?{string.Join("&", query)}"
            : "/v1/workflows";

        var uri = new Uri(url, UriKind.Relative);
        var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WorkflowListResponse<WorkflowRunSummary>>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Failed to parse workflow list response");
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun> SendSignalAsync(
        string runId,
        WorkflowSignal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(signal);

        var response = await _httpClient.PostAsJsonAsync(
            $"/v1/workflows/{Uri.EscapeDataString(runId)}/signal",
            signal,
            _jsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WorkflowRun>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Failed to parse workflow run response");
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun> CancelWorkflowAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/cancel", UriKind.Relative);
        var response = await _httpClient.PostAsync(uri, content: null, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WorkflowRun>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Failed to parse workflow run response");
    }

    /// <inheritdoc/>
    public async Task<WorkflowRun> AbortWorkflowAsync(
        string runId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var request = new AbortWorkflowRequest { Reason = reason };

        var response = await _httpClient.PostAsJsonAsync(
            $"/v1/workflows/{Uri.EscapeDataString(runId)}/abort",
            request,
            _jsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<WorkflowRun>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Failed to parse workflow run response");
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkflowStatusEvent> StreamEventsAsync(
        string runId,
        int? startingAfter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var url = startingAfter.HasValue
            ? $"/v1/workflows/{Uri.EscapeDataString(runId)}/events?starting_after={startingAfter.Value}"
            : $"/v1/workflows/{Uri.EscapeDataString(runId)}/events";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? eventType = null;
        string? data = null;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrEmpty(line))
            {
                // Empty line means end of event
                if (data != null && eventType != null)
                {
                    var evt = ParseEvent(eventType, data);
                    if (evt != null)
                    {
                        yield return evt;
                    }
                }

                eventType = null;
                data = null;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventType = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data = line[5..].Trim();
            }
        }

        // Handle any remaining event
        if (data != null && eventType != null)
        {
            var evt = ParseEvent(eventType, data);
            if (evt != null)
            {
                yield return evt;
            }
        }
    }

    private WorkflowStatusEvent? ParseEvent(string eventType, string data)
    {
        try
        {
            // The event data should be parseable as WorkflowStatusEvent (polymorphic)
            return JsonSerializer.Deserialize<WorkflowStatusEvent>(data, _jsonOptions);
        }
        catch (JsonException ex)
        {
            // Log but don't crash on malformed events
            Console.WriteLine($"Failed to parse workflow event ({eventType}): {ex.Message}");
            return null;
        }
    }
}
