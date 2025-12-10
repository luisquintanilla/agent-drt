// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using AgentContracts;
using AgentContracts.Telemetry;
using AgentContracts.Workflows;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// HTTP client for calling back to the Gateway's workflow state service.
/// Implements IWorkflowStateService over HTTP.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection")]
internal sealed class GatewayWorkflowStateClient : IWorkflowStateService
{
    private const string IfMatchHeader = "If-Match";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayWorkflowStateClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client configured to call the Gateway API.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public GatewayWorkflowStateClient(
        HttpClient httpClient,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        this._httpClient = httpClient;
        this._jsonOptions = jsonOptions ?? AgentContractsJsonUtilities.DefaultOptions;
    }

    /// <inheritdoc/>
    public async Task<string> UpdateStatusAsync(
        string runId,
        WorkflowRunStatusUpdate update,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartHttpOperation("update_status", "PUT", $"/v1/workflows/{runId}/state/status");
        activity?.SetTag(TelemetryConstants.WorkflowRunId, runId);
        activity?.SetTag(TelemetryConstants.WorkflowStatus, update.Status.ToString());

        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(update);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/state/status", UriKind.Relative);
        using var request = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = JsonContent.Create(update, mediaType: null, this._jsonOptions)
        };
        AddETagHeader(request, etag);

        return await this.SendStateRequestAsync(runId, request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> RecordStepStartedAsync(
        string runId,
        WorkflowStepStartedRecord step,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartStepExecution(runId, step.StepId, step.ExecutorId, step.ExecutorName);

        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(step);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/state/steps/started", UriKind.Relative);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(step, mediaType: null, this._jsonOptions)
        };
        AddETagHeader(request, etag);

        return await this.SendStateRequestAsync(runId, request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> RecordStepCompletedAsync(
        string runId,
        WorkflowStepCompletedRecord step,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartGrainOperation("record_step_completed", "StateClient", runId);
        activity?.SetTag(TelemetryConstants.StepId, step.StepId);
        activity?.SetTag(TelemetryConstants.StepDurationMs, step.DurationMs);

        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(step);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/state/steps/completed", UriKind.Relative);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(step, mediaType: null, this._jsonOptions)
        };
        AddETagHeader(request, etag);

        return await this.SendStateRequestAsync(runId, request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> RecordPendingRequestAsync(
        string runId,
        PendingExternalRequest request,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(request);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/state/pending-requests", UriKind.Relative);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(request, mediaType: null, this._jsonOptions)
        };
        AddETagHeader(httpRequest, etag);

        return await this.SendStateRequestAsync(runId, httpRequest, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> ClearPendingRequestAsync(
        string runId,
        string requestId,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/state/pending-requests/{Uri.EscapeDataString(requestId)}", UriKind.Relative);
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        AddETagHeader(request, etag);

        return await this.SendStateRequestAsync(runId, request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> SaveCheckpointAsync(
        string runId,
        WorkflowCheckpointData checkpoint,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartSaveCheckpoint(runId, checkpoint.CheckpointId);

        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(checkpoint);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/state/checkpoint", UriKind.Relative);
        using var request = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = JsonContent.Create(checkpoint, mediaType: null, this._jsonOptions)
        };
        AddETagHeader(request, etag);

        return await this.SendStateRequestAsync(runId, request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<WorkflowCheckpointResult?> GetCheckpointAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        using var activity = WorkflowActivitySource.StartLoadCheckpoint(runId);

        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/state/checkpoint", UriKind.Relative);

        try
        {
            var response = await this._httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw WorkflowNotFoundException.ForRunId(runId);
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<WorkflowCheckpointResult>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get checkpoint for workflow '{runId}'.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<string> RecordArtifactAsync(
        string runId,
        WorkflowArtifactRecord artifact,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(artifact);

        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}/state/artifacts", UriKind.Relative);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(artifact, mediaType: null, this._jsonOptions)
        };
        AddETagHeader(request, etag);

        return await this.SendStateRequestAsync(runId, request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string?> GetETagAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        // We get the workflow to retrieve its ETag
        var uri = new Uri($"/v1/workflows/{Uri.EscapeDataString(runId)}", UriKind.Relative);

        try
        {
            var response = await this._httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var run = await response.Content.ReadFromJsonAsync<WorkflowRun>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
            return run?.ETag;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get ETag for workflow '{runId}'.", ex);
        }
    }

    private static void AddETagHeader(HttpRequestMessage request, string? etag)
    {
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation(IfMatchHeader, etag);
        }
    }

    private async Task<string> SendStateRequestAsync(
        string runId,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw WorkflowNotFoundException.ForRunId(runId);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new WorkflowConcurrencyException(
                    runId,
                    request.Headers.IfMatch?.ToString(),
                    null);
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ETagResponse>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
            return result?.ETag ?? throw new InvalidOperationException("Failed to get ETag from response.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to update workflow state for '{runId}'.", ex);
        }
    }
}

/// <summary>
/// Response containing the new ETag after a state update.
/// </summary>
internal sealed class ETagResponse
{
    public string? ETag { get; init; }
}
