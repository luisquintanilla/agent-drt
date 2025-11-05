// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using AgentContracts;
using AgentWebChat.AgentHost.Options;
using AgentWebChat.AgentHost.Utilities;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace AgentWebChat.AgentHost;

/// <summary>
/// Periodically registers this worker (agent host) with the Gateway and deregisters on shutdown.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "This class is instantiated by dependency injection as a hosted service")]
internal sealed class WorkerRegistrationService : IHostedService, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorkerRegistrationService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly WorkerOptions _options;
    private readonly IServer _server;
    private readonly string _hostId;
    private readonly CancellationTokenSource _cts = new();
    private readonly PeriodicTimer _timer;

    private WorkerRegistrationRequest? _registration; // cached registration request
    private Task? _loopTask;
    private bool _isRegistered;

    public WorkerRegistrationService(
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime lifetime,
        ILogger<WorkerRegistrationService> logger,
        IOptions<WorkerOptions> options,
        IServer server)
    {
        this._httpClientFactory = httpClientFactory;
        this._lifetime = lifetime;
        this._logger = logger;
        this._options = options.Value;
        this._server = server;
        this._hostId = this._options.HostId ?? Environment.MachineName;
        var interval = TimeSpan.FromSeconds(Math.Max(1, this._options.HeartbeatIntervalSeconds));
        this._timer = new PeriodicTimer(interval);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        this._loopTask = Task.Run(() => this.RunAsync(this._cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try { this._cts.Cancel(); } catch { }

        if (this._loopTask is not null)
        {
            try { await this._loopTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { this._logger.LogDebug(ex, "WorkerRegistrationService loop ended with exception"); }
        }

        await this.DeregisterAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // We need to await until startup to ensure that the server features are populated
            // See: https://stackoverflow.com/a/48547462/635314
            await this.WaitForApplicationStartedAsync(ct).ConfigureAwait(false);

            this._logger.LogInformation("WorkerRegistrationService started. Gateway={Gateway} HostId={HostId}", this._options.GatewayBaseAddress, this._hostId);

            do
            {
                await this.RegisterAsync(ct).ConfigureAwait(false);
            } while (await this._timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "WorkerRegistrationService encountered an unexpected exception and is stopping");
        }
    }

    private WorkerRegistrationRequest RegistrationInfo
    {
        get
        {
            if (this._registration is not null)
            {
                return this._registration;
            }

            string advertisedBaseAddress;
            string? explicitValue = this._options.AdvertisedBaseAddress;
            if (!string.IsNullOrWhiteSpace(explicitValue))
            {
                advertisedBaseAddress = explicitValue.TrimEnd('/');
            }
            else
            {
                var hostIpAddress = HostAddressResolver.ResolveIPAddressOrDefault();
                var serverUri = this.GetAspNetServerAddress(host: hostIpAddress?.ToString());
                advertisedBaseAddress = serverUri.ToString().TrimEnd('/');
            }

            return this._registration = new WorkerRegistrationRequest
            {
                HostId = this._hostId,
                Endpoint = new Uri(advertisedBaseAddress, UriKind.Absolute).ToString(),
                HealthPath = "/health", // TODO: get these from options. There is no central place to resolve this from, currently, but perhaps we can validate it.
                DiscoveryPath = "/agents",
            };
        }
    }

    private Uri GetAspNetServerAddress(string? host)
    {
        Exception? error = null;
        try
        {
            var addressesFeature = this._server.Features.Get<IServerAddressesFeature>();
            var addresses = addressesFeature?.Addresses;
            if (addresses?.Count > 0)
            {
                string? https = addresses.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
                string chosen = https ?? addresses.First();
                if (Uri.TryCreate(chosen, UriKind.Absolute, out var uri))
                {
                    host ??= uri.Host;
                    bool isLocalhost = (IPAddress.TryParse(host, out var ip) && (IPAddress.Any.Equals(ip) || IPAddress.IPv6Any.Equals(ip))) || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

                    // Only replace localhost with hostname if the gateway is not also localhost and the 'host' parameter is not explicitly provided.
                    if (isLocalhost && host is null)
                    {
                        bool gatewayIsLocalhost = false;
                        if (Uri.TryCreate(this._options.GatewayBaseAddress, UriKind.Absolute, out var gatewayUri))
                        {
                            string gatewayHost = gatewayUri.Host;
                            gatewayIsLocalhost = string.Equals(gatewayHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(gatewayHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(gatewayHost, "::1", StringComparison.OrdinalIgnoreCase) ||
                                                 (IPAddress.TryParse(gatewayHost, out var gatewayIp) && IPAddress.IsLoopback(gatewayIp));
                        }

                        if (!gatewayIsLocalhost)
                        {
                            host = Dns.GetHostName();
                        }
                    }

                    var builder = new UriBuilder(uri) { Host = host };
                    return builder.Uri;
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to derive advertised base address from server features");
            error = ex;
        }

        throw new InvalidOperationException("Unable to determine an advertised base address for this worker. Configure 'Worker:AdvertisedBaseAddress' or ensure Kestrel has explicit listen addresses.", error);
    }

    private async Task RegisterAsync(CancellationToken ct)
    {
        try
        {
            var client = this._httpClientFactory.CreateClient();
            var uri = new Uri(new Uri(this._options.GatewayBaseAddress, UriKind.Absolute), "/workers/registrations");

            using var response = await client.PostAsJsonAsync(uri, this.RegistrationInfo, AgentContractsJsonContext.Default.WorkerRegistrationRequest, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                this._isRegistered = true;
                try
                {
                    var registrationResponse = await response.Content.ReadFromJsonAsync(AgentContractsJsonContext.Default.WorkerRegistrationResponse, ct).ConfigureAwait(false);
                    if (registrationResponse is not null)
                    {
                        this._logger.LogInformation("Worker registered with id {RegistrationId} (base: {BaseAddress})", registrationResponse.RegistrationId, this.RegistrationInfo.Endpoint);
                    }
                }
                catch (Exception ex)
                {
                    this._logger.LogWarning(ex, "Failed to parse registration response");
                }
            }
            else
            {
                try
                {
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    this._logger.LogWarning("Worker registration heartbeat to {Uri} failed with status {Status}. Reason: '{ReasonPhrase}'. Body: '{Body}'", uri, response.StatusCode, response.ReasonPhrase, body);
                }
                catch
                {
                    this._logger.LogWarning("Worker registration heartbeat to {Uri} failed with status {Status}. Reason: '{ReasonPhrase}'", uri, response.StatusCode, response.ReasonPhrase);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            this._logger.LogWarning(ex, "Worker registration heartbeat failure");
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = this._lifetime.ApplicationStarted.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(), tcs);
        await tcs.Task.WaitAsync(ct);
    }

    private async Task DeregisterAsync(CancellationToken cancellationToken)
    {
        if (!this._isRegistered)
        {
            return;
        }

        string endpoint = this.RegistrationInfo.Endpoint;
        try
        {
            var client = this._httpClientFactory.CreateClient();
            var deleteUri = new Uri(new Uri(this._options.GatewayBaseAddress, UriKind.Absolute), $"/workers/registrations?endpoint={endpoint}");
            using var response = await client.DeleteAsync(deleteUri, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                this._logger.LogInformation("Worker deregistered endpoint '{Endpoint}'", endpoint);
            }
            else
            {
                this._logger.LogWarning("Worker deregistration failed for endpoint '{Endpoint}' status {Status}", endpoint, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Worker deregistration failed for endpoint '{Endpoint}'", endpoint);
        }
    }

    public void Dispose()
    {
        try
        {
            this._timer.Dispose();
            this._cts.Cancel();
            this._cts.Dispose();
        }
        catch { }
    }
}
