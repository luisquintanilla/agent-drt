# Python Agent Worker Framework

A lightweight, reusable framework for building Python agents that integrate seamlessly with Microsoft AgentGateway infrastructure.

## Features

✅ **Minimal Boilerplate** - Write 10-20 lines of business logic, not 100+ lines of protocol code  
✅ **Built-in Telemetry** - OpenTelemetry with GenAI semantic conventions enabled by default  
✅ **Framework Agnostic** - Works with Pydantic AI, Agent Framework, OpenAI SDK, or custom code  
✅ **Auto-instrumentation** - Child spans from AI frameworks automatically nested in traces  
✅ **Type Safe** - Full Pydantic validation and Python type hints  
✅ **Production Ready** - Error handling, logging, health checks built-in

## Quick Start

### Installation

For local development, install from the local directory:

```bash
# In your agent project
uv add ../python-agent-worker
```

Or using pip:

```bash
pip install -e ../python-agent-worker
```

### Basic Usage

```python
# my_agent.py
from agent_worker import Worker, WorkerAgent, EventStreamContext

class HelloAgent(WorkerAgent):
    def __init__(self):
        super().__init__(
            name="hello-agent",
            description="Says hello to the user"
        )
    
    async def execute(self, request, context):
        # Extract input
        input_text = request.input if isinstance(request.input, str) else ""
        
        # Generate output with context manager
        async with context:
            await context.emit_text(f"Hello, {input_text}!")
            context.add_usage(input_tokens=3, output_tokens=5)

# Create worker and register agent
worker = Worker(service_name="my-worker")
worker.register_agent(HelloAgent())

# Export FastAPI app
app = worker.app
```

Run with uvicorn:

```bash
uvicorn my_agent:app --port 5100
```

That's it! You now have a fully functional agent with:
- ✅ Gateway-compatible `/v1/entities` and `/v1/responses` endpoints
- ✅ Health check at `/health`
- ✅ OpenTelemetry tracing, metrics, and logging
- ✅ Proper event streaming and error handling

## Using with Pydantic AI

The framework automatically instruments Pydantic AI with GenAI semantic conventions:

```python
from agent_worker import Worker, WorkerAgent, EventStreamContext
from pydantic_ai import Agent
from pydantic import BaseModel

class TravelItinerary(BaseModel):
    location: str
    attractions: list[str]

class TravelAgent(WorkerAgent):
    def __init__(self):
        super().__init__(
            name="travel-agent",
            description="Generates travel itineraries"
        )
        # Pydantic AI agent
        self.ai_agent = Agent(
            "openai:gpt-4",
            output_type=TravelItinerary,
            system_prompt="You are a helpful travel guide."
        )
    
    async def execute(self, request, context):
        input_text = request.input if isinstance(request.input, str) else ""
        
        async with context:
            # Call Pydantic AI - automatically creates child spans with gen_ai.* attributes
            result = await self.ai_agent.run(input_text)
            itinerary = result.output
            
            # Format and stream output
            output = f"Location: {itinerary.location}\n"
            output += f"Attractions: {', '.join(itinerary.attractions)}"
            await context.emit_text(output)

# Set up worker
worker = Worker(service_name="travel-worker")
worker.register_agent(TravelAgent())
app = worker.app
```

When this runs, you'll see nested traces in Aspire:
```
HTTP POST /v1/responses
└── invoke_agent travel-agent
    └── llm.request gpt-4
        ├── gen_ai.operation.name: chat
        ├── gen_ai.request.model: gpt-4
        ├── gen_ai.usage.input_tokens: 45
        └── gen_ai.usage.output_tokens: 120
```

## Event Streaming

The `EventStreamContext` handles all event creation and sequencing:

```python
async def execute(self, request, context):
    async with context:
        # Emit text (all at once)
        await context.emit_text("Here's the result...")
        
        # Emit text in chunks (for streaming effect)
        await context.emit_text(long_output, chunk_size=100)
        
        # Add usage stats
        context.add_usage(input_tokens=10, output_tokens=50)
        
        # Context manager automatically emits:
        # - response.created on enter
        # - response.in_progress on enter
        # - response.output_item.added on enter
        # - response.output_text.done on exit
        # - response.completed on exit (or response.failed if error)
```

## Error Handling

Exceptions are automatically caught and converted to `response.failed` events:

```python
async def execute(self, request, context):
    async with context:
        # If this raises an exception, it's caught by the context manager
        result = await some_risky_operation()
        await context.emit_text(result)
    
    # Error is automatically formatted as:
    # - response.output_text.delta with error message
    # - response.failed with error details
```

## Telemetry

Telemetry is **required and enabled by default**. The framework sets up:

- **Tracing**: All HTTP requests, agent executions, and child operations
- **Metrics**: Request counts, latencies, token usage
- **Logging**: Structured logs with trace context
- **GenAI Conventions**: Automatic `gen_ai.*` attributes for AI operations

### Configuration

Telemetry reads from standard OpenTelemetry environment variables:

```bash
# OTLP endpoint (Aspire sets this automatically)
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# Service name override (optional)
export OTEL_SERVICE_NAME=my-custom-name
```

Or configure programmatically:

```python
worker = Worker(
    service_name="my-worker",
    otlp_endpoint="http://my-collector:4317"
)
```

### Viewing Telemetry

When running with Aspire, telemetry appears automatically in the dashboard:

1. Start Aspire: `aspire run`
2. Open dashboard: http://localhost:15888
3. View traces, metrics, logs for your worker

## API Reference

### `WorkerAgent`

Abstract base class for agents. Subclass and implement `execute()`.

```python
class WorkerAgent(ABC):
    def __init__(self, name: str, description: str): ...
    
    @abstractmethod
    async def execute(
        self,
        request: CreateResponse,
        context: EventStreamContext
    ) -> None: ...
```

### `Worker`

FastAPI orchestrator that manages agents and infrastructure.

```python
class Worker:
    def __init__(
        self,
        service_name: str = "python-agent-worker",
        enable_telemetry: bool = True,
        otlp_endpoint: Optional[str] = None,
    ): ...
    
    def register_agent(self, agent: WorkerAgent): ...
    
    @property
    def app(self) -> FastAPI: ...
```

### `EventStreamContext`

Context manager for event streaming.

```python
class EventStreamContext:
    async def emit_text(
        self,
        text: str,
        chunk_size: Optional[int] = None
    ): ...
    
    def add_usage(
        self,
        input_tokens: int = 0,
        output_tokens: int = 0,
    ): ...
```

### `setup_telemetry()`

Manually configure telemetry (usually not needed - Worker does this automatically).

```python
def setup_telemetry(
    app: FastAPI,
    service_name: str = "python-agent-worker",
    otlp_endpoint: Optional[str] = None,
    enable_pydantic_ai: bool = True,
) -> trace.Tracer: ...
```

## Examples

See the `examples/` directory for complete working examples:

- `examples/hello_agent.py` - Minimal example
- `examples/pydantic_ai_agent.py` - Using Pydantic AI
- `examples/multiple_agents.py` - Multiple agents in one worker

## Testing

Run tests with pytest:

```bash
cd python-agent-worker
uv run pytest
```

## Architecture

The framework separates concerns:

```
Your Agent Code (10-20 lines)
        ↓
WorkerAgent.execute()
        ↓
EventStreamContext (event helpers)
        ↓
Worker (FastAPI + routing)
        ↓
Protocol Models (Pydantic)
        ↓
OpenTelemetry (traces/metrics/logs)
        ↓
AgentGateway
```

All protocol, streaming, and telemetry logic is handled by the framework, not by agent authors.

## Requirements

- Python 3.12+
- FastAPI
- Pydantic
- OpenTelemetry SDK

## License

Copyright (c) Microsoft. All rights reserved.
