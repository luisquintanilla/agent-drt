// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.Web;
using AgentWebChat.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<AgentDiscoveryClient>(client => client.BaseAddress = new Uri("https+http://gateway"));
builder.Services.AddHttpClient<A2AAgentClient>(client => client.BaseAddress = new Uri("https+http://gateway/a2a"));
builder.Services.AddHttpClient<OpenAIResponsesAgentClient>(client => client.BaseAddress = new Uri("https+http://gateway"));
builder.Services.AddHttpClient<OpenAIChatCompletionsAgentClient>(client => client.BaseAddress = new Uri("https+http://gateway"));
builder.Services.AddHttpClient<WorkflowApiClient>(client => client.BaseAddress = new Uri("https+http://gateway"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseOutputCache();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
