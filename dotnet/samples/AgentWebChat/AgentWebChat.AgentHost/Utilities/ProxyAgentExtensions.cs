// Copyright (c) Microsoft. All rights reserved.

namespace AgentWebChat.AgentHost.Utilities;

/// <summary>
/// Extension methods for registering proxy agents that forward requests to remote agents via Gateway.
/// </summary>
public static class ProxyAgentExtensions
{
    /// <summary>
    /// Registers a proxy agent that routes requests to a remote agent through the Gateway.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="proxyKey">The service key for dependency injection.</param>
    /// <param name="targetAgentName">The name of the remote agent to route to.</param>
    /// <param name="description">Optional description. If null, auto-generates "Proxy to {targetAgentName}".</param>
    /// <param name="httpClientName">The name of the configured HttpClient to use. Defaults to "ProxyClient".</param>
    /// <returns>The builder for chaining.</returns>
    public static WebApplicationBuilder AddProxyAgent(
        this WebApplicationBuilder builder,
        string proxyKey,
        string targetAgentName,
        string? description = null,
        string httpClientName = "ProxyClient")
    {
        builder.Services.AddKeyedSingleton(proxyKey, (sp, key) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(httpClientName);

            return new HttpResponseProxyAgent(
                httpClient: httpClient,
                agentName: targetAgentName,
                description: description ?? $"Proxy to {targetAgentName}"
            );
        });

        return builder;
    }
}