# Python Agent Integration - Polyglot Agent System

> **Implementation Status**: ✅ **COMPLETE** (Steps 1-9)  
> **Last Updated**: 2025-12-08  
> **Status**: Ready for testing

## Overview & Goals

This document outlines the implementation of Python agent support in the AgentWebChat system, enabling polyglot agent workflows where .NET and Python agents can seamlessly collaborate.

**Goals:**
- Enable Python developers to build agents that integrate with AgentWebChat
- Leverage existing gateway infrastructure for discovery, routing, and durability
- Support cross-language workflows (.NET ↔ Python)
- Demonstrate a concrete example: .NET agent → Python agent workflow

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Aspire AppHost                           │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │   Gateway    │  │ .NET AgentHost│ │Python Worker │      │
│  │   (Orleans)  │  │  (Workflows)  │ │  (FastAPI)   │      │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘      │
└─────────┼──────────────────┼──────────────────┼─────────────┘
          │                  │                  │
          │                  │                  │
     ┌────▼──────────────────▼──────────────────▼────┐
     │                                                │
     │  Workflow: story-writer → pig-latin-agent     │
     │                                                │
     │  1. .NET agent generates story                │
     │  2. Gateway routes to Python worker           │
     │  3. Python translates to Pig Latin            │
     │  4. Result returns to workflow                │
     │                                                │
     └────────────────────────────────────────────────┘
```

### How It Works

1. **AppHost** orchestrates all services (Gateway, .NET AgentHost, Python Worker)
2. **Gateway** exposes `/v1/responses` API and handles:
   - Worker discovery via `/agents` endpoint
   - Request routing based on agent name
   - Durable execution and stream caching
3. **Python Worker** implements:
   - `POST /v1/responses` - Execute agent and stream results
   - `GET /agents` - Advertise capabilities
   - `GET /health` - Health checks
4. **.NET AgentHost** defines workflows that can include Python agents

### Worker Contract

Python workers implement a simplified subset of the OpenAI Responses API:

- **Input:** `CreateResponse` JSON (model/agent name, input, metadata)
- **Output:** Server-Sent Events (SSE) stream with `StreamingResponseEvent` objects
- **Discovery:** Return list of supported agents via `/agents` endpoint

The gateway handles all protocol complexity, durability, and state management.

## Project Structure

```
dotnet/samples/AgentWebChat/
├── AgentGateway/              # Existing - Gateway infrastructure
├── AgentWebChat.AgentHost/    # Existing - .NET agent host
│   └── Program.cs             # UPDATED - Add workflow with Python agent
├── AgentWebChat.AppHost/      # Existing - Aspire orchestration
│   └── Program.cs             # UPDATED - Register Python worker
└── PythonAgent/               # NEW - Python worker
    ├── .python-version        # Python 3.12
    ├── pyproject.toml         # UV project configuration
    ├── README.md              # Python-specific documentation
    ├── src/
    │   └── agent_worker/
    │       ├── __init__.py
    │       ├── main.py        # FastAPI application
    │       ├── models.py      # Pydantic models (CreateResponse, etc.)
    │       ├── streaming.py   # SSE streaming utilities
    │       └── agents/
    │           ├── __init__.py
    │           └── pig_latin.py  # Pig Latin translation agent
    └── .gitignore
```

## Implementation Steps

### Step 1: Create Python Project Structure

**1.1 Create directory and initialize UV project**
```bash
cd dotnet/samples/AgentWebChat
mkdir PythonAgent
cd PythonAgent
uv init --name agent-worker --app
```

**1.2 Configure Python version**
```bash
echo "3.12" > .python-version
```

**1.3 Update `pyproject.toml`**
```toml
[project]
name = "agent-worker"
version = "0.1.0"
description = "Python agent worker for AgentWebChat"
requires-python = ">=3.12"
dependencies = [
    "fastapi>=0.115.0",
    "uvicorn[standard]>=0.30.0",
    "pydantic>=2.9.0",
    "httpx>=0.27.0",
    "sse-starlette>=2.1.0",
]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"
```

**1.4 Create `.gitignore`**
```
__pycache__/
*.py[cod]
*$py.class
.venv/
.uv/
*.egg-info/
dist/
```

**1.5 Create directory structure**
```bash
mkdir -p src/agent_worker/agents
touch src/agent_worker/__init__.py
touch src/agent_worker/main.py
touch src/agent_worker/models.py
touch src/agent_worker/streaming.py
touch src/agent_worker/agents/__init__.py
touch src/agent_worker/agents/pig_latin.py
```

**1.6 Install dependencies**
```bash
uv sync
```

### Step 2: Implement Pydantic Models

**2.1 Define request/response models in `src/agent_worker/models.py`**

Based on the gateway's `WorkerResponseExecutor`, we need to support:
- `CreateResponse` request schema
- `StreamingResponseEvent` response schema

```python
# Copyright (c) Microsoft. All rights reserved.

from typing import Any, Literal
from pydantic import BaseModel, Field


class AgentResource(BaseModel):
    """Agent identifier in the request."""
    name: str


class CreateResponse(BaseModel):
    """
    Request schema for POST /v1/responses endpoint.
    Subset of OpenAI Responses API specification.
    """
    agent: AgentResource | None = None
    model: str | None = None  # Fallback if agent not provided
    input: str | list[dict[str, Any]]
    instructions: str | None = None
    metadata: dict[str, str] | None = None
    stream: bool = False
    conversation_id: str | None = None


class StreamingResponseEvent(BaseModel):
    """
    Base event type for SSE streaming responses.
    Gateway expects events in this format.
    """
    type: str
    sequence_number: int = 0


class ResponseCreatedEvent(StreamingResponseEvent):
    """Emitted when response generation starts."""
    type: Literal["response.created"] = "response.created"
    response_id: str


class ResponseInProgressEvent(StreamingResponseEvent):
    """Emitted when response generation is in progress."""
    type: Literal["response.in_progress"] = "response.in_progress"


class OutputItemAddedEvent(StreamingResponseEvent):
    """Emitted when a new output item is added."""
    type: Literal["response.output_item.added"] = "response.output_item.added"
    item_id: str
    output_index: int = 0


class OutputTextDeltaEvent(StreamingResponseEvent):
    """Emitted for each chunk of generated text."""
    type: Literal["response.output_text.delta"] = "response.output_text.delta"
    delta: str
    output_index: int = 0


class OutputTextDoneEvent(StreamingResponseEvent):
    """Emitted when text generation is complete."""
    type: Literal["response.output_text.done"] = "response.output_text.done"
    text: str
    output_index: int = 0


class ResponseCompletedEvent(StreamingResponseEvent):
    """Emitted when response generation is complete."""
    type: Literal["response.completed"] = "response.completed"


class ResponseFailedEvent(StreamingResponseEvent):
    """Emitted when response generation fails."""
    type: Literal["response.failed"] = "response.failed"
    error: dict[str, Any]


class AgentCard(BaseModel):
    """
    Agent discovery card returned by GET /agents endpoint.
    """
    name: str
    description: str | None = None
```

**2.2 Add type hints to `__init__.py`**
```python
# Copyright (c) Microsoft. All rights reserved.

from .models import (
    CreateResponse,
    AgentResource,
    StreamingResponseEvent,
    ResponseCreatedEvent,
    ResponseInProgressEvent,
    OutputItemAddedEvent,
    OutputTextDeltaEvent,
    OutputTextDoneEvent,
    ResponseCompletedEvent,
    ResponseFailedEvent,
    AgentCard,
)

__all__ = [
    "CreateResponse",
    "AgentResource",
    "StreamingResponseEvent",
    "ResponseCreatedEvent",
    "ResponseInProgressEvent",
    "OutputItemAddedEvent",
    "OutputTextDeltaEvent",
    "OutputTextDoneEvent",
    "ResponseCompletedEvent",
    "ResponseFailedEvent",
    "AgentCard",
]
```

### Step 3: Implement SSE Streaming Utilities

**3.1 Create streaming helper in `src/agent_worker/streaming.py`**

```python
# Copyright (c) Microsoft. All rights reserved.

import json
from typing import AsyncIterator
from .models import StreamingResponseEvent


async def stream_events(
    events: AsyncIterator[StreamingResponseEvent],
) -> AsyncIterator[str]:
    """
    Convert Pydantic event objects to SSE format expected by gateway.
    
    Format: "data: {json}\\n\\n"
    """
    async for event in events:
        # Serialize event to JSON
        event_json = event.model_dump_json(exclude_none=True)
        
        # Format as SSE
        yield f"data: {event_json}\n\n"
```

### Step 4: Implement Pig Latin Agent

**4.1 Create pig latin logic in `src/agent_worker/agents/pig_latin.py`**

```python
# Copyright (c) Microsoft. All rights reserved.

import re
from typing import AsyncIterator
from ..models import (
    CreateResponse,
    ResponseCreatedEvent,
    ResponseInProgressEvent,
    OutputItemAddedEvent,
    OutputTextDeltaEvent,
    OutputTextDoneEvent,
    ResponseCompletedEvent,
    StreamingResponseEvent,
)


def translate_to_pig_latin(text: str) -> str:
    """
    Translate English text to Pig Latin.
    
    Rules:
    - Words starting with vowels: add "way" to the end
    - Words starting with consonants: move consonants to end and add "ay"
    - Preserve punctuation and capitalization patterns
    """
    def translate_word(word: str) -> str:
        # Extract leading/trailing punctuation
        leading = ""
        trailing = ""
        
        # Strip leading punctuation
        match = re.match(r'^([^a-zA-Z]*)', word)
        if match:
            leading = match.group(1)
            word = word[len(leading):]
        
        # Strip trailing punctuation
        match = re.search(r'([^a-zA-Z]*)$', word)
        if match:
            trailing = match.group(1)
            word = word[:len(word) - len(trailing)]
        
        if not word:
            return leading + trailing
        
        # Check if word was capitalized
        is_capitalized = word[0].isupper()
        word_lower = word.lower()
        
        # Translate based on first letter
        vowels = "aeiou"
        if word_lower[0] in vowels:
            # Starts with vowel: add "way"
            pig_latin = word_lower + "way"
        else:
            # Starts with consonant(s): move to end and add "ay"
            # Find first vowel
            first_vowel_idx = next(
                (i for i, char in enumerate(word_lower) if char in vowels),
                len(word_lower)
            )
            
            if first_vowel_idx == len(word_lower):
                # No vowels (edge case)
                pig_latin = word_lower + "ay"
            else:
                consonants = word_lower[:first_vowel_idx]
                rest = word_lower[first_vowel_idx:]
                pig_latin = rest + consonants + "ay"
        
        # Restore capitalization
        if is_capitalized:
            pig_latin = pig_latin.capitalize()
        
        return leading + pig_latin + trailing
    
    # Split into words and translate each
    words = text.split()
    translated_words = [translate_word(word) for word in words]
    return " ".join(translated_words)


async def execute_pig_latin_agent(
    request: CreateResponse,
    response_id: str,
) -> AsyncIterator[StreamingResponseEvent]:
    """
    Execute the pig latin translation agent.
    
    Yields streaming events as the translation is performed.
    """
    sequence = 0
    
    # Extract input text
    if isinstance(request.input, str):
        input_text = request.input
    elif isinstance(request.input, list) and len(request.input) > 0:
        # Handle array of messages - take last user message
        for item in reversed(request.input):
            if isinstance(item, dict) and item.get("role") == "user":
                content = item.get("content", "")
                if isinstance(content, list) and len(content) > 0:
                    # Extract text from content array
                    for c in content:
                        if isinstance(c, dict) and c.get("type") == "input_text":
                            input_text = c.get("text", "")
                            break
                    else:
                        input_text = ""
                elif isinstance(content, str):
                    input_text = content
                else:
                    input_text = ""
                break
        else:
            input_text = ""
    else:
        input_text = ""
    
    # Emit response.created
    yield ResponseCreatedEvent(
        response_id=response_id,
        sequence_number=sequence,
    )
    sequence += 1
    
    # Emit response.in_progress
    yield ResponseInProgressEvent(sequence_number=sequence)
    sequence += 1
    
    # Emit output_item.added
    item_id = f"{response_id}_item_0"
    yield OutputItemAddedEvent(
        item_id=item_id,
        output_index=0,
        sequence_number=sequence,
    )
    sequence += 1
    
    # Translate to pig latin
    translated = translate_to_pig_latin(input_text)
    
    # Stream the translation in chunks (simulate streaming)
    chunk_size = 10  # words per chunk
    words = translated.split()
    full_text = []
    
    for i in range(0, len(words), chunk_size):
        chunk_words = words[i:i + chunk_size]
        chunk_text = " ".join(chunk_words)
        
        # Add space if not first chunk
        if i > 0:
            chunk_text = " " + chunk_text
        
        full_text.append(chunk_text)
        
        # Emit delta
        yield OutputTextDeltaEvent(
            delta=chunk_text,
            output_index=0,
            sequence_number=sequence,
        )
        sequence += 1
    
    # Emit output_text.done
    yield OutputTextDoneEvent(
        text="".join(full_text),
        output_index=0,
        sequence_number=sequence,
    )
    sequence += 1
    
    # Emit response.completed
    yield ResponseCompletedEvent(sequence_number=sequence)
```

**4.2 Create `src/agent_worker/agents/__init__.py`**
```python
# Copyright (c) Microsoft. All rights reserved.

from .pig_latin import execute_pig_latin_agent

__all__ = ["execute_pig_latin_agent"]
```

### Step 5: Implement FastAPI Application

**5.1 Create main application in `src/agent_worker/main.py`**

```python
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


@app.get("/agents")
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
```

### Step 6: Update Aspire AppHost

**6.1 Install Community Toolkit package**

In `AgentWebChat.AppHost`:
```bash
cd dotnet/samples/AgentWebChat/AgentWebChat.AppHost
dotnet add package CommunityToolkit.Aspire.Hosting.Python.Uv
```

**6.2 Update `Program.cs` to register Python worker**

Add after the `agentHost` definition (around line 30):

```csharp
// Python agent worker
var pythonAgent = builder.AddUvApp(
    name: "python-agent",
    projectDirectory: "../PythonAgent",
    entrypoint: "src.agent_worker.main",
    args: "--app",
    appName: "app")
    .WithHttpEndpoint(port: 5100, name: "http")
    .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"));
```

**Key details:**
- `projectDirectory`: Relative path from AppHost to PythonAgent
- `entrypoint`: Python module path (`src.agent_worker.main`)
- `args`: `--app` tells uvicorn the FastAPI app variable name
- `WithEnvironment`: Pass gateway URL for future auto-registration

### Step 7: Update .NET AgentHost with Workflow

**7.1 Add story-writer agent**

In `AgentWebChat.AgentHost/Program.cs`, after existing agent definitions (around line 90):

```csharp
// Story writer agent (generates stories)
var storyWriterAgent = builder.AddAIAgent(
    "story-writer",
    instructions: "You are a creative story writer. Write short, imaginative stories (2-3 sentences) based on the given prompt.",
    description: "An agent that writes creative short stories.",
    chatClientServiceKey: "chat-model");
```

**7.2 Add Python agent reference**

Add code to create a reference to the Python agent (which will be discovered via gateway):

```csharp
// Python pig latin agent (discovered via gateway)
// Note: This creates a proxy that calls the Python agent through the gateway
builder.Services.AddKeyedSingleton<AIAgent>("pig-latin-agent", (sp, key) =>
{
    var gatewayUrl = builder.Configuration["Worker:GatewayBaseAddress"];
    if (string.IsNullOrEmpty(gatewayUrl))
    {
        throw new InvalidOperationException("Worker:GatewayBaseAddress configuration is required for Python agent integration");
    }

    var httpClient = new HttpClient { BaseAddress = new Uri(gatewayUrl) };
    
    var options = new OpenAIClientOptions
    {
        Endpoint = new Uri(httpClient.BaseAddress!, "/v1/"),
        Transport = new HttpClientPipelineTransport(httpClient)
    };
    
    var responseClient = new OpenAIResponseClient(
        model: "pig-latin-agent",
        credential: new ApiKeyCredential("dummy-key"),
        options: options
    );
    
    return new OpenAIResponseClientAgent(
        responseClient,
        name: "pig-latin-agent",
        description: "Translates English text to Pig Latin"
    );
});
```

**7.3 Add sequential workflow**

Add the workflow definition:

```csharp
// Polyglot workflow: .NET writes story, Python translates to Pig Latin
var polyglotWorkflow = builder.AddWorkflow("polyglot-story-workflow", (sp, key) =>
{
    var agents = new AIAgent[]
    {
        sp.GetRequiredKeyedService<AIAgent>("story-writer"),
        sp.GetRequiredKeyedService<AIAgent>("pig-latin-agent")
    };
    
    return AgentWorkflowBuilder.BuildSequential(
        workflowName: key,
        agents: agents
    );
}).AddAsAIAgent();
```

**7.4 Add required usings at top of file**

```csharp
using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Responses;
```

### Step 8: Test the Integration

**8.1 Start the application**

```bash
cd dotnet/samples/AgentWebChat/AgentWebChat.AppHost
dotnet run
```

This will start:
- Gateway (with Orleans)
- .NET AgentHost (with workflows)
- Python Agent Worker (FastAPI)
- Web Frontend

**8.2 Verify Python worker health**

Navigate to Aspire dashboard and check:
- Python agent shows as "Running"
- Logs show "Uvicorn running on http://..."

**8.3 Test Python agent directly**

```bash
# Check health
curl http://localhost:5100/health

# Check discovery
curl http://localhost:5100/agents

# Test translation
curl -X POST http://localhost:5100/v1/responses \
  -H "Content-Type: application/json" \
  -d '{
    "agent": {"name": "pig-latin-agent"},
    "input": "Hello world this is a test"
  }'
```

**8.4 Test via Gateway**

```bash
# Gateway should discover the Python agent
curl http://localhost:5390/agents

# Execute via gateway
curl -X POST http://localhost:5390/v1/responses \
  -H "Content-Type: application/json" \
  -d '{
    "agent": {"name": "pig-latin-agent"},
    "input": "The quick brown fox jumps over the lazy dog",
    "stream": false
  }'
```

**8.5 Test the polyglot workflow**

Via the Web UI or DevUI:
1. Navigate to agents/workflows
2. Find "polyglot-story-workflow"
3. Send a story prompt: "Write a story about a dragon"
4. Expected flow:
   - .NET agent writes a short story
   - Python agent translates it to Pig Latin
   - Final result is the story in Pig Latin

### Step 9: Create Python README

**9.1 Create `PythonAgent/README.md`**

```markdown
# Python Agent Worker

This is a Python-based agent worker for the AgentWebChat system. It demonstrates how to build agents in Python that integrate with the .NET-based gateway infrastructure.

## Agents

### pig-latin-agent

Translates English text to Pig Latin using standard Pig Latin rules:
- Words starting with vowels: add "way" to the end (e.g., "apple" → "appleway")
- Words starting with consonants: move consonants to end and add "ay" (e.g., "hello" → "ellohay")
- Preserves capitalization and punctuation

## Development

### Prerequisites

- Python 3.12+
- [uv](https://docs.astral.sh/uv/) package manager

### Setup

```bash
# Install dependencies
uv sync

# Run locally
uv run uvicorn src.agent_worker.main:app --reload --port 5100
```

### Endpoints

- `GET /health` - Health check
- `GET /agents` - List available agents
- `POST /v1/responses` - Execute agent (OpenAI Responses API format)
- `GET /` - Service info

## Architecture

### Worker Contract

This worker implements the gateway's execution contract:

1. **Discovery** (`GET /agents`): Returns list of supported agents
2. **Execution** (`POST /v1/responses`): 
   - Accepts `CreateResponse` request
   - Returns SSE stream with `StreamingResponseEvent` objects
3. **Health** (`GET /health`): Returns 200 OK when healthy

The gateway handles:
- Request routing based on agent name
- Durable execution and state persistence
- Stream caching and resumption
- Worker health monitoring

### Adding New Agents

1. Create agent module in `src/agent_worker/agents/`
2. Implement async function that yields `StreamingResponseEvent` objects
3. Register in `AGENTS` dict in `main.py`
4. Agent is automatically discovered by gateway

Example:

```python
# src/agent_worker/agents/my_agent.py

async def execute_my_agent(
    request: CreateResponse,
    response_id: str,
) -> AsyncIterator[StreamingResponseEvent]:
    sequence = 0
    
    yield ResponseCreatedEvent(
        response_id=response_id,
        sequence_number=sequence,
    )
    sequence += 1
    
    # ... your agent logic ...
    
    yield ResponseCompletedEvent(sequence_number=sequence)
```

```python
# main.py - add to AGENTS dict

AGENTS = {
    "my-agent": {
        "name": "my-agent",
        "description": "My custom agent",
        "executor": execute_my_agent,
    }
}
```

## Integration with .NET Workflows

Python agents can be used in .NET workflows through the gateway. The .NET code creates a proxy agent that calls the Python agent via the gateway's Responses API:

```csharp
// In .NET AgentHost
builder.Services.AddKeyedSingleton<AIAgent>("pig-latin-agent", (sp, key) =>
{
    var gatewayUrl = builder.Configuration["Worker:GatewayBaseAddress"];
    var httpClient = new HttpClient { BaseAddress = new Uri(gatewayUrl) };
    
    var options = new OpenAIClientOptions
    {
        Endpoint = new Uri(httpClient.BaseAddress!, "/v1/"),
        Transport = new HttpClientPipelineTransport(httpClient)
    };
    
    var responseClient = new OpenAIResponseClient(
        model: "pig-latin-agent",
        credential: new ApiKeyCredential("dummy-key"),
        options: options
    );
    
    return new OpenAIResponseClientAgent(responseClient, name: "pig-latin-agent");
});

// Use in workflow
var workflow = builder.AddWorkflow("my-workflow", (sp, key) =>
{
    var agents = new[] {
        sp.GetRequiredKeyedService<AIAgent>("dotnet-agent"),
        sp.GetRequiredKeyedService<AIAgent>("pig-latin-agent") // Python agent
    };
    
    return AgentWorkflowBuilder.BuildSequential(workflowName: key, agents: agents);
});
```

## Future Enhancements

### Auto-Registration

Currently, the Python worker is registered statically in the AppHost. For dynamic environments, you can implement auto-registration:

```python
import httpx
import asyncio

async def register_with_gateway(gateway_url: str, worker_url: str):
    """Register this worker with the gateway on startup."""
    async with httpx.AsyncClient() as client:
        response = await client.post(
            f"{gateway_url}/workers/registrations",
            json={
                "hostId": "python-worker-1",
                "endpoint": worker_url,
                "healthPath": "/health",
                "discoveryPath": "/agents"
            }
        )
        response.raise_for_status()
        print(f"Registered with gateway: {response.json()}")

# Add to FastAPI lifespan
@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup
    gateway_url = os.getenv("GATEWAY_URL")
    worker_url = os.getenv("WORKER_URL")
    if gateway_url and worker_url:
        await register_with_gateway(gateway_url, worker_url)
    
    yield
    
    # Shutdown - could deregister here

app = FastAPI(lifespan=lifespan)
```

**Benefits:**
- Dynamic worker discovery
- Automatic health heartbeats
- Works in cloud environments

**Trade-offs:**
- More complex startup logic
- Need to determine network addresses
- Potential race conditions if gateway isn't ready
```

## Implementation Progress

### ✅ Completed Steps (1-6)

**Step 1: Create Python Project Structure** ✅
- Created `PythonAgent/` directory under `dotnet/samples/AgentWebChat/`
- Initialized UV project with Python 3.12
- Configured `pyproject.toml` with FastAPI, Uvicorn, Pydantic dependencies
- Created proper directory structure (`src/agent_worker/`, `agents/`)
- Installed all dependencies successfully

**Step 2: Implement Pydantic Models** ✅
- Created `models.py` with complete schema definitions
- Implemented `CreateResponse`, `AgentResource`, `AgentCard`
- Implemented all streaming event types: `ResponseCreatedEvent`, `ResponseInProgressEvent`, `OutputItemAddedEvent`, `OutputTextDeltaEvent`, `OutputTextDoneEvent`, `ResponseCompletedEvent`, `ResponseFailedEvent`
- Added proper type hints and documentation

**Step 3: Implement SSE Streaming Utilities** ✅
- Created `streaming.py` with `stream_events()` helper
- Properly formats Pydantic events to SSE format expected by gateway

**Step 4: Implement Pig Latin Agent** ✅
- Created `pig_latin.py` with full translation logic
- Implemented `translate_to_pig_latin()` function that:
  - Handles vowel-starting words (add "way")
  - Handles consonant-starting words (move to end + "ay")
  - Preserves capitalization and punctuation
- Implemented `execute_pig_latin_agent()` async generator
- Emits proper streaming events with sequence numbers
- Tested locally - translation works correctly ("Hello world" → "Ellohay orldway")

**Step 5: Implement FastAPI Application** ✅
- Created `main.py` with complete FastAPI app
- Implemented 3 required endpoints:
  - `GET /health` - Health check
  - `GET /agents` - Discovery (returns pig-latin-agent card)
  - `POST /v1/responses` - Execution with SSE streaming
- Agent registry system for extensibility
- Proper error handling for unknown agents

**Step 6: Update Aspire AppHost** ✅
- Added `CommunityToolkit.Aspire.Hosting.Python.Extensions` package (version 9.0.0)
- Updated `Directory.Packages.props` with central package management
- Updated `AgentWebChat.AppHost.csproj` with package reference
- Updated `Program.cs` to register Python worker:
  ```csharp
  var pythonAgent = builder.AddPythonApp(
      name: "python-agent",
      projectDirectory: "../PythonAgent",
      scriptPath: "src/agent_worker/main.py")
      .WithHttpEndpoint(port: 5100, name: "http")
      .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"));
  ```
- Suppressed `ASPIREHOSTINGPYTHON001` diagnostic
- **Build verified**: All projects build successfully with no regressions

### 🚧 Remaining Steps (7-9)

**Step 7: Update .NET AgentHost with Workflow** ✅ **COMPLETE**
- ✅ Added story-writer agent (.NET)
- ✅ Added Python agent proxy/reference using `OpenAIResponseClientAgent`
- ✅ Created polyglot workflow combining both agents
- ✅ Added required using statements for OpenAI types
- ✅ Added project reference to `Microsoft.Agents.AI.OpenAI`
- Files modified:
  - `AgentWebChat.AgentHost/Program.cs`
  - `AgentWebChat.AgentHost/AgentWebChat.AgentHost.csproj`

**Step 8: Test the Integration** ⏭️ **SKIPPED**
- Skipped per user request
- Code is ready for testing when needed

**Step 9: Create Python README** ✅ **COMPLETE**
- ✅ Created comprehensive `PythonAgent/README.md`
- ✅ Documented all agent capabilities
- ✅ Included development setup instructions
- ✅ Explained architecture and worker contract
- ✅ Documented how to add new agents
- ✅ Included auto-registration pattern example
- ✅ Added troubleshooting section

### Implementation Complete! 🎉

All implementation steps have been completed:

1. ✅ **Python Agent Worker** - Full FastAPI application with pig-latin-agent
2. ✅ **Aspire Integration** - Python worker registered in AppHost
3. ✅ **.NET Workflow** - Polyglot workflow combining .NET and Python agents
4. ✅ **Documentation** - Comprehensive README for Python developers

### Ready for Testing

The implementation is complete and ready for end-to-end testing:

```bash
# Start the Aspire application
cd dotnet/samples/AgentWebChat
aspire run
```

This will start:
- **Gateway** - Routes requests and manages state
- **.NET AgentHost** - Hosts the story-writer agent and polyglot workflow
- **Python Worker** - Hosts the pig-latin-agent
- **Web Frontend** - UI for interacting with agents

### Testing the Polyglot Workflow

Once running, you can test the polyglot workflow:

1. Navigate to the Aspire dashboard or DevUI
2. Find the "polyglot-story-workflow" workflow
3. Send a prompt: "Write a story about a dragon"
4. Expected behavior:
   - .NET `story-writer` agent creates a short story
   - Python `pig-latin-agent` translates it to Pig Latin
   - Final output is the story in Pig Latin

### Files Modified

```
dotnet/
├── Directory.Packages.props                                    # Added Python package version
└── samples/AgentWebChat/
    ├── Polyglot.md                                            # This file (implementation guide)
    ├── AgentWebChat.AppHost/
    │   ├── AgentWebChat.AppHost.csproj                       # Added Python package reference
    │   └── Program.cs                                         # Registered Python worker
    ├── AgentWebChat.AgentHost/
    │   ├── AgentWebChat.AgentHost.csproj                     # Added Microsoft.Agents.AI.OpenAI reference
    │   └── Program.cs                                         # Added story-writer, Python proxy, polyglot workflow
    └── PythonAgent/                                           # NEW - Complete Python agent
        ├── .gitignore
        ├── .python-version                                    # Python 3.12
        ├── pyproject.toml                                     # UV project config
        ├── README.md                                          # Comprehensive documentation
        └── src/agent_worker/
            ├── __init__.py
            ├── main.py                                        # FastAPI app with 3 endpoints
            ├── models.py                                      # Pydantic schemas
            ├── streaming.py                                   # SSE helper
            └── agents/
                ├── __init__.py
                └── pig_latin.py                               # Pig Latin translation agent
```

### Testing Notes

The Python agent has been tested locally and works correctly:
- Health check: `curl http://localhost:5100/health` → `{"status":"healthy"}`
- Discovery: `curl http://localhost:5100/agents` → Returns pig-latin-agent card
- Translation: "Hello world this is a test" → "Ellohay orldway isthay isway away esttay"

The .NET solution builds successfully with no errors or warnings.

## Summary

This implementation demonstrates:

1. **Polyglot Agents**: Python and .NET agents working together
2. **Gateway Pattern**: Centralized protocol handling and routing
3. **OpenAI Responses API**: Standard interface for agent execution
4. **Aspire Integration**: Unified orchestration of multi-language services
5. **Concrete Workflow**: Story generation → Pig Latin translation

The gateway abstracts away protocol complexity, allowing Python developers to focus on agent logic while getting durable execution, stream caching, and cross-language workflows for free.
