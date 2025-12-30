// Copyright (c) Microsoft. All rights reserved.

using AgentContracts;
using AgentWebChat.AgentHost;
using AgentWebChat.AgentHost.Custom;
using AgentWebChat.AgentHost.DurableAgents.Utilities;
using AgentWebChat.AgentHost.Options;
using AgentWebChat.AgentHost.Utilities;
using AgentWebChat.AgentHost.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Add a singleton capturing this worker process metadata (instance id + host id)
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<WorkerOptions>>().Value;
    string hostId = options.HostId ?? Environment.MachineName;
    return new WorkerProcessMetadata { InstanceId = Guid.NewGuid(), HostId = hostId };
});

bool enableWorkerRegistration = builder.Configuration.GetValue<bool>("AgentRuntime:RegisterWorker");
if (enableWorkerRegistration)
{
    // Register worker registration background service
    builder.Services.AddHostedService<WorkerRegistrationService>();
}

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.AddDevUI();

// Configure chat message store using Conversations API via AgentGateway
// The gateway base address is provided by Aspire's service discovery
var conversationsBaseAddress = /*"http://localhost:5390"*/builder.Configuration["Worker:GatewayBaseAddress"];
if (!string.IsNullOrWhiteSpace(conversationsBaseAddress))
{
    builder.Services.AddHttpClient<ConversationsApiClient>(client => client.BaseAddress = new Uri(conversationsBaseAddress));
    builder.Services.AddConversationStorageClient();
}

// Add services to the container.
builder.Services.AddProblemDetails();

// Add DevUI services
builder.AddDevUI();

// Add OpenAI services
builder.AddOpenAIChatCompletions();
builder.AddOpenAIResponses();

var pirateAgentBuilder = builder.AddAIAgent(
    "pirate",
    instructions: "You are a pirate. Speak like a pirate",
    description: "An agent that speaks like a pirate.",
    chatClientServiceKey: "chat-model")
    .WithAITool(new CustomAITool())
    .WithAITool(new CustomFunctionTool())
    .WithInMemoryThreadStore();

builder.AddKeyedChatClient("chat-model").UseDurableFunctionInvocation();
builder.AddAIAgent("config-rollout", (sp, key) =>
{
    var chatClient = sp.GetRequiredKeyedService<IChatClient>("chat-model");
    return new ConfigRolloutAgent(
        chatClient,
        sp.GetRequiredService<ILogger<ConfigRolloutAgent>>());
});

builder.AddAIAgent("pirate", (sp, key) =>
{
    var chatClient = sp.GetRequiredKeyedService<IChatClient>("chat-model");
    return new DurableChatClientAgent(
        chatClient,
        instructions: "Speak like a pirate in all responses.",
        name: "pirate");
});

// Workflow consisting of multiple specialized agents
var chemistryAgent = builder.AddAIAgent("chemist",
    instructions: "You are a chemistry expert. Answer thinking from the chemistry perspective",
    description: "An agent that helps with chemistry.",
    chatClientServiceKey: "chat-model");

var mathsAgent = builder.AddAIAgent("mathematician",
    instructions: "You are a mathematics expert. Answer thinking from the maths perspective",
    description: "An agent that helps with mathematics.",
    chatClientServiceKey: "chat-model");

var literatureAgent = builder.AddAIAgent("literator",
    instructions: "You are a literature expert. Answer thinking from the literature perspective",
    description: "An agent that helps with literature.",
    chatClientServiceKey: "chat-model");

var scienceSequentialWorkflow = builder.AddWorkflow("science-sequential-workflow", (sp, key) =>
{
    List<IHostedAgentBuilder> usedAgents = [chemistryAgent, mathsAgent, literatureAgent];
    var agents = usedAgents.Select(ab => sp.GetRequiredKeyedService<AIAgent>(ab.Name));
    return AgentWorkflowBuilder.BuildSequential(workflowName: key, agents: agents);
}).AddAsAIAgent();

var scienceConcurrentWorkflow = builder.AddWorkflow("science-concurrent-workflow", (sp, key) =>
{
    List<IHostedAgentBuilder> usedAgents = [chemistryAgent, mathsAgent, literatureAgent];
    var agents = usedAgents.Select(ab => sp.GetRequiredKeyedService<AIAgent>(ab.Name));
    return AgentWorkflowBuilder.BuildConcurrent(workflowName: key, agents: agents);
}).AddAsAIAgent();

builder.AddWorkflow("nonAgentWorkflow", (sp, key) =>
{
    List<IHostedAgentBuilder> usedAgents = [pirateAgentBuilder, chemistryAgent];
    var agents = usedAgents.Select(ab => sp.GetRequiredKeyedService<AIAgent>(ab.Name));
    return AgentWorkflowBuilder.BuildSequential(workflowName: key, agents: agents);
});

// Story writer agent (generates creative stories)
var storyWriterAgent = builder.AddAIAgent(
    "story-writer",
    instructions: "You are a creative story writer. Write short, imaginative stories (2-3 sentences) based on the given prompt.",
    description: "An agent that writes creative short stories.",
    chatClientServiceKey: "chat-model");

// Register HttpClient for Gateway communication
builder.Services.AddHttpClient("GatewayClient", (sp, client) =>
{
    var gatewayUrl = builder.Configuration["Worker:GatewayBaseAddress"];
    if (!string.IsNullOrEmpty(gatewayUrl))
    {
        client.BaseAddress = new Uri(gatewayUrl);
    }
});

// Register proxy agent that calls Python agent through Gateway
// NOTE: Using AddKeyedSingleton instead of AddAIAgent to prevent circular discovery!
// The Gateway should discover pig-latin-agent from PythonAgent, not from AgentHost
builder.Services.AddKeyedSingleton<AIAgent>("pig-latin-proxy", (sp, key) =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("GatewayClient");

    return new HttpResponseProxyAgent(
        httpClient: httpClient,
        agentName: "pig-latin-agent", // Routes to Python agent via Gateway
        description: "Proxy to Python pig-latin-agent"
    );
});

// Polyglot workflow: .NET writes story, Python translates to Pig Latin
var polyglotWorkflow = builder.AddWorkflow("polyglot-story-workflow", (sp, key) =>
{
    var agents = new AIAgent[]
    {
        sp.GetRequiredKeyedService<AIAgent>("story-writer"),
        sp.GetRequiredKeyedService<AIAgent>("pig-latin-proxy") // Use proxy instead of direct pig-latin-agent
    };

    return AgentWorkflowBuilder.BuildSequential(
        workflowName: key,
        agents: agents
    );
});

// Register Itinieray Planning Python Agent
builder.Services.AddKeyedSingleton("travel-itinerary-proxy", (sp, key) =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("GatewayClient");

    return new HttpResponseProxyAgent(
        httpClient: httpClient,
        agentName: "travel-itinerary-agent", // Routes to Python agent via Gateway
        description: "Proxy to Python travel-itinerary-agent"
    );
});

// Write stories based on travel itineraries
var travelWorkflow = builder.AddWorkflow("travel-journal-workflow", (sp, key) =>
{
    var agents = new AIAgent[]
    {
        sp.GetRequiredKeyedService<HttpResponseProxyAgent>("travel-itinerary-proxy"), // Use proxy instead of direct travel-itinerary-agent
        sp.GetRequiredKeyedService<AIAgent>("story-writer"),
    };

    return AgentWorkflowBuilder.BuildSequential(
        workflowName: key,
        agents: agents
    );
});

builder.Services.AddKeyedSingleton("NonAgentAndNonmatchingDINameWorkflow", (sp, key) =>
{
    List<IHostedAgentBuilder> usedAgents = [pirateAgentBuilder, chemistryAgent];
    var agents = usedAgents.Select(ab => sp.GetRequiredKeyedService<AIAgent>(ab.Name));
    return AgentWorkflowBuilder.BuildSequential(workflowName: "random-name", agents: agents);
});

builder.Services.AddSingleton<AIAgent>(sp =>
{
    var chatClient = sp.GetRequiredKeyedService<IChatClient>("chat-model");
    return new ChatClientAgent(chatClient, name: "default-agent", instructions: "you are a default agent.");
});

builder.Services.AddKeyedSingleton<AIAgent>("my-di-nonmatching-agent", (sp, name) =>
{
    var chatClient = sp.GetRequiredKeyedService<IChatClient>("chat-model");
    return new ChatClientAgent(
        chatClient,
        name: "some-random-name", // demonstrating registration can be different for DI and actual agent
        instructions: "you are a dependency inject agent. Tell me all about dependency injection.");
});

builder.Services.AddKeyedSingleton<AIAgent>("my-di-matchingname-agent", (sp, name) =>
{
    if (name is not string nameStr)
    {
        throw new NotSupportedException("Name should be passed as a key");
    }

    var chatClient = sp.GetRequiredKeyedService<IChatClient>("chat-model");
    return new ChatClientAgent(
        chatClient,
        name: nameStr, // demonstrating registration with the same name
        instructions: "you are a dependency inject agent. Tell me all about dependency injection.");
});

// Register HITL Workflow Host Service and Marketing Content Workflow
builder.Services.AddSingleton(sp =>
{
    var host = new WorkflowHostService(
        sp,
        sp.GetRequiredService<ILogger<WorkflowHostService>>());

    // Register available workflows
    host.RegisterWorkflow<MarketingContentWorkflow>("marketing-content");

    return host;
});

// Register entity provider for HITL workflows to appear in DevUI
builder.Services.AddSingleton<IEntityProvider, WorkflowHostEntityProvider>();

var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Agents API"));

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// attach a2a with simple message communication
app.MapA2A(pirateAgentBuilder, path: "/a2a/pirate");

app.MapDevUI();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

app.MapOpenAIChatCompletions(pirateAgentBuilder);

// Worker meta endpoint used by gateway to uniquely identify this process
app.MapGet("/worker/meta", (WorkerProcessMetadata meta) => Results.Ok(meta));

// Map HITL Workflow Host endpoints (called by Gateway to execute/resume workflows)
app.MapWorkflowHost();

app.MapDefaultEndpoints();
app.Run();
