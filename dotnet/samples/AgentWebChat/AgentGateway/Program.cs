// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Net.Http;
using AgentContracts;
using Microsoft.Agents.AI.Hosting.OpenAI;
using AgentGateway.Conversations;
using AgentGateway.DevUI;
using AgentGateway.Health;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using AgentGateway.Utilities;
using Microsoft.Agents.AI.DevUI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.Storage;

var builder = WebApplication.CreateBuilder(args);

// Configure Agent Gateway options from configuration
builder.Services.Configure<AgentGatewayOptions>(options =>
{
    var section = builder.Configuration.GetSection(AgentGatewayOptions.SectionName);
    section.Bind(options);

    // Handle Workers as either string array or object array
    var workersSection = section.GetSection("Workers");
    if (workersSection.Exists())
    {
        options.Workers.Clear();
        foreach (var child in workersSection.GetChildren())
        {
            // Try to bind as object first
            var worker = new WorkerOptions();
            child.Bind(worker);

            // If Endpoint is not set, treat the whole value as an endpoint string
            if (string.IsNullOrEmpty(worker.Endpoint) && !string.IsNullOrEmpty(child.Value))
            {
                worker.Endpoint = child.Value;
            }

            if (!string.IsNullOrEmpty(worker.Endpoint))
            {
                options.Workers.Add(worker);
            }
        }
    }
});

builder.AddKeyedAzureBlobServiceClient("state");
builder.AddKeyedAzureTableServiceClient("reminders");
builder.AddKeyedAzureTableServiceClient("clustering");

builder.AddServiceDefaults();

// Add custom health check for Agent Gateway
builder.Services.AddHealthChecks()
    .AddCheck<AgentGatewayHealthCheck>("agent_gateway");
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton<ForwardingHttpClientProvider>();

// Configure Orleans
builder.UseOrleans();

builder.Services.Configure<ClusterOptions>(options =>
{
    options.ClusterId = "agent-webchat-cluster1";
    options.ServiceId = "AgentWebChatService1";
});

builder.Services.AddSerializer(serializerBuilder =>
{
    // Configure System.Text.Json serialization for all Microsoft.Agents.* and AgentGateway types
    // This uses AgentGatewayJsonUtilities which chains together all the necessary type resolvers
    // including OpenAIJsonUtilities (OpenAI Hosting types), AIJsonUtilities (Microsoft.Extensions.AI), and grain states
    serializerBuilder.AddJsonSerializer(
        isSupported: type => type.Namespace?.StartsWith("Microsoft.Agents", StringComparison.Ordinal) == true ||
                            type.Namespace?.StartsWith("AgentContracts", StringComparison.Ordinal) == true ||
                            type.Namespace?.StartsWith("AgentGateway", StringComparison.Ordinal) == true,
        jsonSerializerOptions: AgentGatewayJsonUtilities.DefaultOptions);
});

// Register System.Text.Json-based grain storage serializer
builder.Services.AddSingleton<IGrainStorageSerializer>(sp => new SystemTextJsonGrainStorageSerializer(AgentGatewayJsonUtilities.DefaultOptions));
builder.Services.Configure<ClusterMembershipOptions>(o =>
{
    o.NumMissedTableIAmAliveLimit = 2;
    o.IAmAliveTablePublishTimeout = TimeSpan.FromSeconds(10);
});

// Register conversation storage - choose between in-memory and Orleans-backed
// For production use, uncomment the Orleans implementation:
builder.Services.AddSingleton<IConversationStorage, OrleansConversationStorage>();
builder.Services.AddSingleton<IAgentConversationIndex, OrleansAgentConversationIndex>();

// Register IChatClient for responses service
// Configure via connection strings (supports OpenAI, Azure OpenAI, Ollama, Azure AI Inference)
builder.AddChatClient("chat-model");

// Register responses service - use Orleans-backed implementation
builder.Services.AddSingleton<IResponsesService, OrleansResponsesService>();

// Option 1: Local execution using ChatClientAgent (default)
//builder.Services.AddSingleton<IResponseExecutor, LocalChatClientResponseExecutor>();

// Option 2: Worker forwarding via HTTP (comment out option 1 and uncomment this)
builder.Services.AddSingleton<IResponseExecutor, WorkerResponseExecutor>();

// Configure JSON serialization to use snake_case naming (web conventions)
// Uses source-generated JSON serializer context for better performance and trimming support
// Chains with AgentContractsJsonUtilities for worker registration and conversation types
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Start with AgentContracts options which includes WorkerHostJsonContext and AIJsonUtilities
    var contractsOptions = AgentContractsJsonUtilities.DefaultOptions;
    options.SerializerOptions.TypeInfoResolverChain.Add(contractsOptions.TypeInfoResolver!);

    // Add Gateway-specific context at the front for priority
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AgentGatewayJsonContext.Default);
});

builder.Services.AddOpenApi();

// Registry + background processing for worker heartbeat & health checks
builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<WorkerDiscoveryCache>>();
    return new WorkerDiscoveryCache(sp.GetRequiredService<HttpClient>(), logger);
});
builder.Services.AddSingleton<WorkerRegistry>();
builder.Services.AddHostedService<WorkerHealthCheckService>();
builder.Services.AddSingleton<WorkerHttpForwarder>();

// Register entity provider for DevUI entities API
builder.Services.AddSingleton<IEntityProvider, WorkerRegistryEntityProvider>();
builder.AddOpenAIResponses();

var app = builder.Build();

app.MapDefaultEndpoints();

// Enable static files middleware - required for DevUI assets
// DevUI assets are automatically available at /_content/Microsoft.Agents.AI.DevUI/
app.UseStaticFiles();

// Conditionally map worker management endpoints based on configuration
var gatewayOptions = app.Services.GetRequiredService<IOptions<AgentGatewayOptions>>().Value;
if (gatewayOptions.EnableRuntimeRegistration)
{
    app.MapWorkerManagement();
}

// Map forwarding endpoints to workers (eg, A2A) via IHttpForwarder
app.MapA2AForwarder();

// Map Conversations API endpoints
app.MapOpenAIConversations();

// Map Responses API endpoints using the shared implementation from Microsoft.Agents.AI.Hosting.OpenAI
app.MapOpenAIResponses();

// Map DevUI
app.MapDevUI();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Redirect root to DevUI
app.MapGet("/", () => Results.Redirect("/devui"));

// Log default worker if configured
var workerRegistry = app.Services.GetRequiredService<WorkerRegistry>();
if (workerRegistry.DefaultWorker is not null)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation(
        "Default worker configured. Endpoint={Endpoint} HostId={HostId}",
        workerRegistry.DefaultWorker.Endpoint,
        workerRegistry.DefaultWorker.HostId);
}

app.Run();
