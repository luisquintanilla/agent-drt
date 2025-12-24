# Python Agent Worker

This is a Python-based agent worker for the AgentWebChat system. It demonstrates how to build agents in Python that integrate with the .NET-based gateway infrastructure.

## Agents

### pig-latin-agent

Translates English text to Pig Latin using standard Pig Latin rules:
- Words starting with vowels: add "way" to the end (e.g., "apple" → "appleway")
- Words starting with consonants: move consonants to end and add "ay" (e.g., "hello" → "ellohay")
- Preserves capitalization and punctuation

**Example:**
```
Input:  "Hello world this is a test"
Output: "Ellohay orldway isthay isway away esttay"
```

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

### OpenTelemetry Configuration

This Python agent is configured to export telemetry (traces, metrics, and logs) to the Aspire dashboard via OpenTelemetry Protocol (OTLP).

**How it works:**
- The `telemetry.py` module configures OpenTelemetry exporters for tracing, metrics, and logging
- FastAPI is automatically instrumented to capture HTTP requests and responses
- The agent reads the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable (automatically set by Aspire)
- All telemetry data is exported to the Aspire dashboard for visualization

**What gets captured:**
- **Traces**: HTTP requests to all endpoints, including execution time and status codes
- **Metrics**: Request counts, response times, and other performance indicators
- **Logs**: Application logs with correlation to traces for easy debugging

**Configuration:**
The OpenTelemetry configuration is automatically initialized in `main.py`:
```python
from .telemetry import configure_telemetry

# Configure OpenTelemetry for Aspire dashboard integration
tracer = configure_telemetry(app, service_name="python-agent")
```

When running with Aspire, no additional configuration is needed. The Aspire AppHost automatically provides:
- `OTEL_EXPORTER_OTLP_ENDPOINT` - The OTLP endpoint URL
- `OTEL_SERVICE_NAME` - The service name for telemetry identification
- Other OpenTelemetry environment variables as needed

**Local Development:**
To test telemetry locally without Aspire:
```bash
# Set the OTLP endpoint (e.g., local Aspire dashboard)
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# Run the application
uv run uvicorn src.agent_worker.main:app --port 5100
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

### Request/Response Flow

```
Client → Gateway → Python Worker
         ↓
    1. POST /v1/responses
    2. Agent name: "pig-latin-agent"
    3. Input: "Hello world"
         ↓
    Python Worker:
    - Validates agent exists
    - Executes pig_latin.translate_to_pig_latin()
    - Streams events back:
      * response.created
      * response.in_progress
      * response.output_item.added
      * response.output_text.delta (chunked)
      * response.output_text.done
      * response.completed
         ↓
    Gateway:
    - Caches all events
    - Returns to client
```

### Event Streaming

The worker uses Server-Sent Events (SSE) to stream responses:

```python
# FastAPI endpoint
@app.post("/v1/responses")
async def create_response(request: CreateResponse):
    # Execute agent
    event_stream = execute_pig_latin_agent(request, response_id)
    
    # Convert to SSE format
    sse_stream = stream_events(event_stream)
    
    return EventSourceResponse(sse_stream)
```

Each event is formatted as:
```
data: {"type":"response.output_text.delta","sequence_number":3,"delta":"Ellohay ","output_index":0}

```

## Adding New Agents

### Step 1: Create Agent Module

Create a new file in `src/agent_worker/agents/`:

```python
# src/agent_worker/agents/my_agent.py

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

async def execute_my_agent(
    request: CreateResponse,
    response_id: str,
) -> AsyncIterator[StreamingResponseEvent]:
    """Execute my custom agent."""
    sequence = 0
    
    # Extract input
    input_text = request.input if isinstance(request.input, str) else ""
    
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
    yield OutputItemAddedEvent(
        item_id=f"{response_id}_item_0",
        output_index=0,
        sequence_number=sequence,
    )
    sequence += 1
    
    # Your agent logic here
    result = f"Processed: {input_text}"
    
    # Emit output delta
    yield OutputTextDeltaEvent(
        delta=result,
        output_index=0,
        sequence_number=sequence,
    )
    sequence += 1
    
    # Emit output done
    yield OutputTextDoneEvent(
        text=result,
        output_index=0,
        sequence_number=sequence,
    )
    sequence += 1
    
    # Emit response.completed
    yield ResponseCompletedEvent(sequence_number=sequence)
```

### Step 2: Register in Main App

Update `src/agent_worker/main.py`:

```python
from .agents import execute_pig_latin_agent, execute_my_agent

AGENTS = {
    "pig-latin-agent": {
        "name": "pig-latin-agent",
        "description": "Translates English text to Pig Latin",
        "executor": execute_pig_latin_agent,
    },
    "my-agent": {
        "name": "my-agent",
        "description": "My custom agent",
        "executor": execute_my_agent,
    }
}
```

### Step 3: Update Exports

Update `src/agent_worker/agents/__init__.py`:

```python
from .pig_latin import execute_pig_latin_agent
from .my_agent import execute_my_agent

__all__ = ["execute_pig_latin_agent", "execute_my_agent"]
```

Your agent is now automatically discovered by the gateway!

## Integration with .NET Workflows

Python agents can be used in .NET workflows through the gateway. The .NET code creates a proxy agent that calls the Python agent via the gateway's Responses API.

### Example: Polyglot Workflow

```csharp
// In .NET AgentHost/Program.cs

// Story writer agent (.NET)
var storyWriterAgent = builder.AddAIAgent(
    "story-writer",
    instructions: "Write short, imaginative stories (2-3 sentences).",
    chatClientServiceKey: "chat-model");

// Python pig latin agent (proxy)
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

// Polyglot workflow: .NET → Python
var workflow = builder.AddWorkflow("polyglot-story-workflow", (sp, key) =>
{
    var agents = new AIAgent[]
    {
        sp.GetRequiredKeyedService<AIAgent>("story-writer"),
        sp.GetRequiredKeyedService<AIAgent>("pig-latin-agent")
    };
    
    return AgentWorkflowBuilder.BuildSequential(workflowName: key, agents: agents);
}).AddAsAIAgent();
```

### How It Works

1. User sends request to "polyglot-story-workflow"
2. .NET `story-writer` agent generates a story
3. Output is passed to `pig-latin-agent` proxy
4. Proxy sends request to Gateway's `/v1/responses` with `model: "pig-latin-agent"`
5. Gateway discovers Python worker supports "pig-latin-agent"
6. Gateway forwards to Python worker's `/v1/responses`
7. Python worker executes translation and streams back
8. Gateway caches and returns to .NET
9. Workflow completes with Pig Latin story

## Testing

### Direct Testing

```bash
# Health check
curl http://localhost:5100/health

# Discover agents
curl http://localhost:5100/agents

# Execute agent
curl -X POST http://localhost:5100/v1/responses \
  -H "Content-Type: application/json" \
  -d '{
    "agent": {"name": "pig-latin-agent"},
    "input": "The quick brown fox jumps over the lazy dog"
  }'
```

### Via Gateway

```bash
# Gateway discovers Python agents
curl http://localhost:5390/agents

# Execute through gateway
curl -X POST http://localhost:5390/v1/responses \
  -H "Content-Type: application/json" \
  -d '{
    "agent": {"name": "pig-latin-agent"},
    "input": "The quick brown fox jumps over the lazy dog",
    "stream": false
  }'
```

## Deployment

### Running with Aspire

The Python worker is registered in the Aspire AppHost:

```csharp
// In AgentWebChat.AppHost/Program.cs
var pythonAgent = builder.AddPythonApp(
    name: "python-agent",
    projectDirectory: "../PythonAgent",
    scriptPath: "src/agent_worker/main.py")
    .WithHttpEndpoint(port: 5100, name: "http")
    .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"));
```

Start everything with:
```bash
cd AgentWebChat
aspire run
```

## Future Enhancements

### Auto-Registration

Currently, the Python worker is registered statically in the AppHost. For dynamic environments, you can implement auto-registration:

```python
import os
import httpx
from contextlib import asynccontextmanager

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
- No manual configuration needed

**Trade-offs:**
- More complex startup logic
- Need to determine network addresses
- Potential race conditions if gateway isn't ready

## Troubleshooting

### Worker Not Discovered

1. Check worker is running: `curl http://localhost:5100/health`
2. Check agents endpoint: `curl http://localhost:5100/agents`
3. Check gateway can reach worker (network/firewall)
4. Verify gateway worker registration if using auto-registration

### Streaming Issues

1. Ensure Content-Type is `text/event-stream`
2. Check events are formatted correctly: `data: {json}\n\n`
3. Verify sequence numbers increment properly
4. Check all required events are emitted (created, in_progress, completed)

### Import Errors

```bash
# Reinstall dependencies
uv sync

# Check Python version
python --version  # Should be 3.12+
```

## License

Copyright (c) Microsoft. All rights reserved.
