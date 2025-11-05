// Copyright (c) Microsoft. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AgentGateway;

internal static class A2AHttpApi
{
    public static void MapA2AForwarder(this WebApplication app)
    {
        // A2A agent endpoints - using route parameters to capture agent name
        app.MapGet("/a2a/{agent}", (HttpContext context, WorkerHttpForwarder forwarder, string agent) => forwarder.ForwardRequestAsync(context, agent));
        app.MapPost("/a2a/{agent}", (HttpContext context, WorkerHttpForwarder forwarder, string agent) => forwarder.ForwardRequestAsync(context, agent));

        // /v1/card endpoint - Agent discovery
        app.MapGet("/a2a/{agent}/v1/card", (HttpContext context, WorkerHttpForwarder forwarder, string agent) => forwarder.ForwardRequestAsync(context, agent));

        // /v1/tasks/{id} endpoint
        app.MapGet("/a2a/{agent}/v1/tasks/{id}", (HttpContext context, WorkerHttpForwarder forwarder, string agent, string id) => forwarder.ForwardRequestAsync(context, agent, id));

        // /v1/tasks/{id}:cancel endpoint
        app.MapPost("/a2a/{agent}/v1/tasks/{id}:cancel", (HttpContext context, WorkerHttpForwarder forwarder, string agent, string id) => forwarder.ForwardRequestAsync(context, agent, id));

        // /v1/tasks/{id}:subscribe endpoint
        app.MapGet("/a2a/{agent}/v1/tasks/{id}:subscribe", (HttpContext context, WorkerHttpForwarder forwarder, string agent, string id) => forwarder.ForwardRequestAsync(context, agent, id));

        // /v1/tasks/{id}/pushNotificationConfigs endpoint - POST
        app.MapPost("/a2a/{agent}/v1/tasks/{id}/pushNotificationConfigs", (HttpContext context, WorkerHttpForwarder forwarder, string agent, string id) => forwarder.ForwardRequestAsync(context, agent, id));

        // /v1/tasks/{id}/pushNotificationConfigs/{notificationConfigId?} endpoint - GET
        app.MapGet("/a2a/{agent}/v1/tasks/{id}/pushNotificationConfigs/{notificationConfigId?}", (HttpContext context, WorkerHttpForwarder forwarder, string agent, string id, string? notificationConfigId) => forwarder.ForwardRequestAsync(context, agent, id));

        // /v1/message:send endpoint
        app.MapPost("/a2a/{agent}/v1/message:send", (HttpContext context, WorkerHttpForwarder forwarder, string agent) => forwarder.ForwardRequestAsync(context, agent));

        // /v1/message:stream endpoint
        app.MapPost("/a2a/{agent}/v1/message:stream", (HttpContext context, WorkerHttpForwarder forwarder, string agent) => forwarder.ForwardRequestAsync(context, agent));
    }
}
