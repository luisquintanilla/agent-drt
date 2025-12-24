// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var chatModelName = "gpt-4.1";
var azOpenAiResource = builder.AddParameterFromConfiguration("AzureOpenAIName", "AzureOpenAI:Name");
var azOpenAiResourceGroup = builder.AddParameterFromConfiguration("AzureOpenAIResourceGroup", "AzureOpenAI:ResourceGroup");
var chatModel = builder.AddAIModel("chat-model").AsAzureOpenAI(chatModelName, o => o.AsExisting(azOpenAiResource, azOpenAiResourceGroup));

var storage = builder.AddAzureStorage("storage").RunAsEmulator(emulator => emulator.WithDataBindMount());
var grainState = storage.AddBlobs("state");
var reminders = storage.AddTables("reminders");

var orleans = builder.AddOrleans("orleans-silo")
    .WithGrainStorage("Default", grainState)
    .WithReminders(reminders)
    .WithDevelopmentClustering();

// Gateway sits in front of agent host.
var gateway = builder.AddProject<Projects.AgentGateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithReference(orleans)
    .WithReference(chatModel);

// Python agent worker
var pythonAgent = builder.AddUvicornApp(
    "python-agent",
    "../PythonAgent",
    "src.agent_worker.main:app")
    .WithUv()
    .WithEndpoint("http", endpoint => endpoint.Port = 5100)
    .WithReference(chatModel)
    .WaitFor(chatModel)
    .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithEnvironment("MODEL_NAME", chatModelName)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", $"https://{azOpenAiResource}.openai.azure.com/");

// Agent host depends on gateway
var agentHost = builder.AddProject<Projects.AgentWebChat_AgentHost>("agenthost")
    .WithHttpEndpoint(name: "devui")
    .WithUrlForEndpoint("devui", (url) => new() { Url = "/devui", DisplayText = "Dev UI" })
    .WithEnvironment("Worker__GatewayBaseAddress", gateway.GetEndpoint("http")!)
    .WithReference(chatModel);

// Web front-end depends on gateway (not agent host directly anymore)
builder.AddProject<Projects.AgentWebChat_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(gateway)
    .WaitFor(gateway);

// MarketR React frontend for workflow management
// builder.AddNpmApp("marketr", "../MarketR", "dev")
//     .WithReference(gateway)
//     .WithHttpEndpoint(port: 5173, env: "PORT")
//     .WithExternalHttpEndpoints()
//     .WaitFor(gateway);

// MonitorDashboard React frontend for system monitoring
// builder.AddNpmApp("monitor", "../MonitorDashboard", "dev")
//     .WithReference(gateway)
//     .WithHttpEndpoint(port: 5174, env: "PORT")
//     .WithExternalHttpEndpoints()
//     .WaitFor(gateway);

builder.Build().Run();
