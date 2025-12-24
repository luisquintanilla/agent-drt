# Python Agent Worker

This is a Python-based agent worker for the AgentWebChat system. It demonstrates how to build agents in Python that integrate with the .NET-based gateway infrastructure using the **python-agent-worker framework**.

## Framework-Based Architecture

This worker has been refactored to use the `python-agent-worker` framework, which provides:

✅ **Minimal Boilerplate** - Each agent is ~50-150 lines (previously 200-300+ lines)  
✅ **Built-in Telemetry** - OpenTelemetry with GenAI semantic conventions enabled by default  
✅ **Auto-instrumentation** - Pydantic AI spans automatically nested with gen_ai.* attributes  
✅ **Protocol Abstraction** - No need to understand gateway protocol or event streaming  
✅ **Error Handling** - Automatic conversion of exceptions to proper protocol events

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

### travel-itinerary-agent

Generates detailed travel itineraries using Pydantic AI and Azure OpenAI. Demonstrates:
- Integration with Pydantic AI framework
- Automatic GenAI semantic conventions in traces
- Structured output parsing
- Token counting with tiktoken

## Development

### Prerequisites

- Python 3.12+
- [uv](https://docs.astral.sh/uv/) package manager

### Setup

```bash
# Install dependencies (includes local python-agent-worker framework)
uv sync

# Run locally
uv run uvicorn src.main:app --reload --port 5100
```

### Project Structure

```
PythonAgent/
├── src/
│   ├── main.py              # Worker setup (30 lines)
│   └── agents/
│       ├── pig_latin.py     # Pig Latin agent (~130 lines, mostly business logic)
│       └── travel_itinerary.py  # Travel agent (~170 lines, mostly business logic)
├── pyproject.toml           # Dependencies (includes local framework)
└── README.md
```

### OpenTelemetry Configuration

This agent uses the `python-agent-worker` framework, which configures OpenTelemetry automatically:

**What gets captured:**
- **Traces**: HTTP requests, agent execution, and child AI operations
  - HTTP POST /v1/responses spans
  - Agent execution spans (with custom attributes)
  - Pydantic AI spans with GenAI semantic conventions:
    - `gen_ai.operation.name`: "chat"
    - `gen_ai.request.model`: Model name
    - `gen_ai.usage.input_tokens`: Input token count
    - `gen_ai.usage.output_tokens`: Output token count
    - `gen_ai.input.messages`: Prompts (if enabled)
    - `gen_ai.output.messages`: Completions (if enabled)
- **Metrics**: Request counts, response times, token usage
- **Logs**: Application logs with trace context for correlation

**Configuration:**
The framework automatically reads standard OpenTelemetry environment variables:
```python
# In src/main.py
from agent_worker import Worker

worker = Worker(service_name="python-agent")  # Telemetry enabled by default
```

When running with Aspire, no additional configuration is needed. The Aspire AppHost automatically provides:
- `OTEL_EXPORTER_OTLP_ENDPOINT` - The OTLP endpoint URL (defaults to http://localhost:4317)
- `OTEL_SERVICE_NAME` - Optional service name override

**Local Development:**
To test telemetry locally without Aspire:
```bash
# Set the OTLP endpoint (e.g., local OTLP collector)
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# Run the application
uv run uvicorn src.main:app --port 5100
```

### Endpoints

The framework automatically provides these endpoints:

- `GET /health` - Health check
- `GET /v1/entities` - List available agents (discovery)
- `POST /v1/responses` - Execute agent (OpenAI Responses API format)
- `GET /` - Service info

## Architecture

### Framework-Based Design

This worker uses the `python-agent-worker` framework:

```
src/main.py (Worker setup)
    ↓
Worker.register_agent(PigLatinAgent())
Worker.register_agent(TravelItineraryAgent())
    ↓
python-agent-worker framework handles:
- FastAPI setup and routing
- Protocol event streaming
- OpenTelemetry instrumentation
- Error handling
    ↓
Agent developers only write:
- execute() method with business logic
- Input extraction
- Output generation
```

### Worker Contract

The framework implements the gateway's execution contract:

1. **Discovery** (`GET /v1/entities`): Returns list of supported agents
2. **Execution** (`POST /v1/responses`):
   - Accepts `CreateResponse` request
   - Returns SSE stream with `StreamingResponseEvent` objects
3. **Health** (`GET /health`): Returns 200 OK when healthy

The gateway handles:
- Request routing based on agent name
- Durable execution and state persistence
- Stream caching and resumption
- Worker health monitoring

## Adding New Agents

With the framework, adding a new agent is simple:

### Step 1: Create Agent Class

Create a new file in `src/agents/`:

```python
# src/agents/my_agent.py
from agent_worker import WorkerAgent, EventStreamContext

class MyAgent(WorkerAgent):
    def __init__(self):
        super().__init__(
            name="my-agent",
            description="My custom agent"
        )
    
    async def execute(self, request, context: EventStreamContext):
        # Extract input
        input_text = request.input if isinstance(request.input, str) else ""
        
        # Use context manager for event streaming
        async with context:
            result = f"Processed: {input_text}"
            await context.emit_text(result)
            context.add_usage(input_tokens=5, output_tokens=10)
```

### Step 2: Register in Main

Update `src/main.py`:

```python
from agents.my_agent import MyAgent

# Add to worker
worker.register_agent(MyAgent())
```

That's it! The framework handles all protocol and telemetry concerns.

## Testing

### Direct Testing

```bash
# Health check
curl http://localhost:5100/health

# Discover agents
curl http://localhost:5100/v1/entities

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
curl http://localhost:5390/v1/entities

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
    scriptPath: "src/main.py")
    .WithHttpEndpoint(port: 5100, name: "http")
    .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"));
```

Start everything with:
```bash
cd AgentWebChat
aspire run
```

## Integration with .NET Workflows

Python agents can be used in .NET workflows through the gateway. The .NET code creates a proxy agent that calls the Python agent via the gateway's Responses API. See the AgentWebChat documentation for examples of polyglot workflows.

## Troubleshooting

### Worker Not Discovered

1. Check worker is running: `curl http://localhost:5100/health`
2. Check agents endpoint: `curl http://localhost:5100/v1/entities`
3. Check gateway can reach worker (network/firewall)

### Import Errors

```bash
# Reinstall dependencies
uv sync

# Check Python version
python --version  # Should be 3.12+
```

## Framework Documentation

For more information about the `python-agent-worker` framework, see:
- `../python-agent-worker/README.md` - Framework documentation
- `../python-agent-worker/examples/` - Example agents
- `../docs/PYTHON_AGENT_WORKER_TECHNICAL_SPEC.md` - Technical specification

## License

Copyright (c) Microsoft. All rights reserved.
