# Copyright (c) Microsoft. All rights reserved.

import uuid
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from sse_starlette.sse import EventSourceResponse
from .models import CreateResponse, AgentCard
from .streaming import stream_events
from .agents import execute_pig_latin_agent

app = FastAPI(
    title="Agent Worker",
    description="Python agent worker for AgentWebChat",
    version="0.1.0",
)


# Registry of available agents
AGENTS = {
    "pig-latin-agent": {
        "name": "pig-latin-agent",
        "description": "Translates English text to Pig Latin",
        "executor": execute_pig_latin_agent,
    }
}


@app.get("/health")
async def health_check():
    """
    Health check endpoint.
    
    Gateway's WorkerHealthCheckService probes this endpoint.
    """
    return {"status": "healthy"}


@app.get("/v1/entities")
async def list_agents() -> list[AgentCard]:
    """
    Agent discovery endpoint.
    
    Gateway's WorkerDiscoveryCache queries this to find
    which agents this worker supports.
    """
    return [
        AgentCard(
            name=agent["name"],
            description=agent["description"],
        )
        for agent in AGENTS.values()
    ]


@app.post("/v1/responses")
async def create_response(
    request: CreateResponse,
    http_request: Request,
):
    """
    Execute agent and stream response.
    
    This is the main execution endpoint that the gateway's
    WorkerResponseExecutor forwards requests to.
    
    Returns SSE stream with StreamingResponseEvent objects.
    """
    # Extract agent name from request
    agent_name = None
    if request.agent and request.agent.name:
        agent_name = request.agent.name
    elif request.model:
        agent_name = request.model
    elif request.metadata and "entity_id" in request.metadata:
        agent_name = request.metadata["entity_id"]
    
    if not agent_name or agent_name not in AGENTS:
        return JSONResponse(
            status_code=404,
            content={
                "error": {
                    "message": f"Agent '{agent_name}' not found",
                    "type": "invalid_request_error",
                }
            },
        )
    
    # Get response ID from header (set by gateway) or generate
    response_id = http_request.headers.get("X-Response-ID") or f"resp_{uuid.uuid4().hex}"
    
    # Get agent executor
    agent = AGENTS[agent_name]
    executor = agent["executor"]
    
    # Execute agent and stream events
    event_stream = executor(request, response_id)
    
    # Convert to SSE format
    sse_stream = stream_events(event_stream)
    
    return EventSourceResponse(sse_stream)


@app.get("/")
async def root():
    """Root endpoint with service info."""
    return {
        "service": "AgentWebChat Python Worker",
        "version": "0.1.0",
        "agents": len(AGENTS),
    }
