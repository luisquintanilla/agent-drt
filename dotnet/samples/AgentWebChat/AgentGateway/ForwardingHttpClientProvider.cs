// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace AgentGateway;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection")]
internal sealed class ForwardingHttpClientProvider
{
    public HttpMessageInvoker HttpClient { get; }

    public ForwardingHttpClientProvider() : this(new ForwarderHttpClientFactory()) { }

    public ForwardingHttpClientProvider(IForwarderHttpClientFactory factory)
    {
        this.HttpClient = factory.CreateClient(new ForwarderHttpClientContext
        {
            NewConfig = HttpClientConfig.Empty
        });
    }
}
