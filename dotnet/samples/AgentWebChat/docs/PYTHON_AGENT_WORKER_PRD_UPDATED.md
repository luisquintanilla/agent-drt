# Python Agent Worker Framework - PRD (UPDATED)

**Document Status:** Draft  
**Date:** December 24, 2025  
**Audience:** Engineering team, product stakeholders  

---

## Executive Summary

This document outlines the design and implementation plan for `python-agent-worker`, a lightweight, implementation-agnostic microframework that enables Python developers to build AI agents that integrate seamlessly with the Microsoft Agent Gateway infrastructure (`AgentGateway`).

The framework abstracts away the complexity of the gateway communication protocol and provides first-class OpenTelemetry observability with GenAI semantic conventions support, allowing developers to focus entirely on their agent's business logic while supporting any underlying LLM framework (Pydantic AI, Agent Framework, OpenAI Agents SDK, or custom implementations).

### Key Goals

1. **Reduce boilerplate by 80%** - Developers write 10-20 lines of code per agent instead of 60-100
2. **Default Telemetry** - OpenTelemetry integration is built-in, enabled by default, not optional
3. **GenAI Semantic Conventions** - Full support for OpenTelemetry GenAI semantic conventions, including automatic integration with Pydantic AI and other framework telemetry
4. **Framework-agnostic** - Works with any Python LLM/agent framework or custom code
5. **Clean Separation** - Core package isolated in separate directory for independent testing, maintenance, and potential PyPI publication
6. **Seamless gateway integration** - Automatic discovery, routing, and protocol compliance
7. **Production-ready** - Error handling, logging, telemetry support built-in
8. **Accessible** - Clear abstractions with minimal learning curve

---

## Problem Statement

Currently, building a Python worker for the Agent Gateway requires developers to:

1. **Understand the gateway protocol** - `CreateResponse` request format, `StreamingResponseEvent` schema
2. **Set up FastAPI infrastructure** - Boilerplate endpoints (`/health`, `/v1/entities`, `/v1/responses`)
3. **Implement event streaming** - Manual sequence numbering, response status tracking, event wrapping
4. **Handle errors and edge cases** - Input extraction from multiple formats, request validation
5. **Manually configure telemetry** - OpenTelemetry setup for Aspire integration (often skipped, reducing observability)
6. **Navigate semantic conventions** - Understand how to integrate with GenAI semantic conventions from child LLM libraries

This results in:  

- **Per-agent overhead:** 60-100 lines of protocol/event boilerplate per agent
- **Knowledge barrier:** Developers must understand OpenAI Responses API format, HTTP streaming, and OTEL setup
- **Code duplication:** Every agent re-implements the same event streaming logic
- **Testing friction:** Hard to test agent logic in isolation from framework concerns
- **Observability gaps:** Telemetry often omitted due to complexity, hindering production monitoring
- **Framework integration challenges:** Unclear how to leverage built-in telemetry from Pydantic AI, Agent Framework, etc.

### Current State (PythonAgent Example)

```
PythonAgent/src/agent_worker/
├── main.py              (87 lines - FastAPI setup, routing)
├── models. py            (108 lines - Pydantic protocol models)
├── streaming.py         (18 lines - SSE conversion)
├── telemetry.py         (91 lines - OpenTelemetry setup)
└── agents/
    ├── pig_latin.py     (108 lines - 50 lines boilerplate, 58 logic)
    └── travel_itinerary.py (118 lines - 50 lines boilerplate, 68 logic)
```

**Result:** ~42% of code is non-reusable protocol boilerplate.  Telemetry must be manually configured in every worker.

---

## Solution Overview

### Three Core Components

#### 1. `WorkerAgent` - Abstract Base Class
Minimal interface for agents to implement.   Developers define: 
- Agent name and description (metadata)
- `execute()` method that processes gateway requests and emits events

#### 2. `Worker` - FastAPI Orchestrator
Handles all infrastructure concerns:
- FastAPI app setup (auto-configures all endpoints)
- Agent discovery and registration
- Request routing to correct agent
- Response streaming and error handling
- Integrated telemetry (no separate setup needed)

#### 3. `EventStreamContext` - Event Helper
Context manager that eliminates event boilerplate:
- Automatic event creation and sequencing
- Text chunking and streaming
- Structured data support
- Error handling (ResponseFailedEvent)

#### 4. `setup_telemetry()` - Observability Function
One-line setup for complete observability:
- OpenTelemetry SDK configuration (traces, metrics, logs)
- OTLP exporter to Aspire or any compatible backend
- FastAPI automatic instrumentation
- Logging integration with trace context
- Child framework instrumentation (Pydantic AI, etc.) automatically nested

### Architecture Diagram

```
Developer Code (Business Logic Only)
    ↓
WorkerAgent. execute()
    ↓
EventStreamContext (Helper)
    ├─ emit_text(chunk)
    ├─ emit_structured(data)
    └─ stream() → Iterator[StreamingResponseEvent]
    ↓
Worker (FastAPI + Routing)
    ├─ GET /health
    ├─ GET /v1/entities (agent discovery)
    └─ POST /v1/responses (agent execution)
    ↓
setup_telemetry() ← All instrumentation
    ├─ OpenTelemetry TracerProvider
    ├─ FastAPI instrumentation
    ├─ Logging instrumentation
    └─ Child library spans (Pydantic AI, etc.)
    ↓
Aspire Dashboard / OTLP Collector
```

### OpenTelemetry Integration (Default)

**Telemetry is mandatory and enabled by default. ** One-line setup:

```python
from python_agent_worker import Worker, setup_telemetry

worker = Worker([MyAgent()])
setup_telemetry(worker.app, service_name="my-worker")
worker.run()
```

Configuration via environment variables (standard OTEL):
- `OTEL_EXPORTER_OTLP_ENDPOINT` (default: `http://localhost:4317`)
- `OTEL_SERVICE_NAME` (default: `agent-worker`)
- `OTEL_TRACES_EXPORTER`, `OTEL_METRICS_EXPORTER`, `OTEL_LOGS_EXPORTER` for selective disabling

### GenAI Semantic Conventions Compliance

When agents use frameworks like **Pydantic AI**:
- Pydantic AI automatically emits spans conforming to [OpenTelemetry GenAI Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/)
- These spans are **automatically nested** under the parent HTTP and agent execution spans
- Attributes such as `gen_ai.system`, `gen_ai.request.model`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens` flow directly into traces and metrics
- No developer configuration needed; automatic and transparent

Example trace tree: 

```
POST /v1/responses                      (FastAPI span)
  └─ agent.execute travel-planner       (Worker span)
      └─ invoke_agent travel-planner    (Pydantic AI, GenAI semantic conventions)
          ├─ llm.request gpt-4o         
          │   └─ gen_ai.request.model = "gpt-4o"
          │       gen_ai.request.max_tokens = 2000
          └─ llm.response gpt-4o
              └─ gen_ai. usage.input_tokens = 150
                  gen_ai.usage.output_tokens = 280
```

---

## Directory Structure

The framework is colocated with but **cleanly separated from** the sample application code:

```
dotnet/samples/AgentWebChat/
├── AgentGateway/                      # Existing - Gateway service
├── AgentWebChat. AgentHost/            # Existing - . NET agents
├── AgentWebChat. AppHost/              # Existing - Aspire orchestration
│
├── python-agent-worker/               # NEW - Framework package (separate directory)
│   ├── agent_worker/                  # Package source
│   │   ├── __init__.py                # Public API
│   │   ├── worker.py                  # Worker class
│   │   ├── agent. py                   # WorkerAgent ABC
│   │   ├── helpers.py                 # EventStreamContext, extract_input
│   │   ├── telemetry.py               # setup_telemetry() + get_tracer()
│   │   ├── protocol/
│   │   │   ├── __init__.py
│   │   │   └── models.py              # Pydantic protocol models
│   │   └── server/
│   │       ├── __init__.py
│   │       └── app.py                 # FastAPI app factory
│   ├── tests/
│   │   ├── test_worker.py
│   │   ├── test_agent.py
│   │   ├── test_helpers.py
│   │   ├── test_telemetry. py
│   │   ├── conftest.py
│   │   └── integration/
│   │       └── test_end_to_end.py
│   ├── examples/
│   │   ├── simple_agent.py
│   │   ├── with_pydantic_ai.py
│   │   ├── with_agent_framework. py
│   │   └── with_telemetry.py
│   ├── docs/
│   │   ├── api.md
│   │   ├── quickstart.md
│   │   ├── examples.md
│   │   ├── telemetry.md
│   │   └── semantic_conventions.md
│   ├── pyproject.toml                 # Independently publishable
│   ├── README.md
│   ├── LICENSE
│   └── CHANGELOG.md
│
└── PythonAgent/                       # NEW - Sample application (business logic only)
    ├── pyproject.toml                 # Depends on ../python-agent-worker
    ├── . python-version
    ├── README.md
    └── src/
        └── agents/
            ├── __init__.py
            ├── pig_latin.py           # ~20 lines business logic
            ├── travel_itinerary.py    # ~25 lines business logic
            └── main.py                # ~10 lines:  Worker setup + telemetry
```

**Benefits of this structure:**
- ✅ **Separate ownership**: Framework maintainers vs. sample authors
- ✅ **Independent testing**: Test framework in isolation, or with sample
- ✅ **Future PyPI publishing**: `python-agent-worker/` can be published as-is
- ✅ **Clear responsibility**: PythonAgent = business logic only
- ✅ **Rapid development**: Can iterate on agents without modifying framework
- ✅ **Extensibility**: Others can create agents in different projects using the same package

---

## Scope:   Phase 1 (MVP)

### Included

✅ **Core Framework**
- `WorkerAgent` abstract base class
- `Worker` class with FastAPI integration
- `EventStreamContext` helper
- `extract_input()` utility function

✅ **Protocol Support**
- `CreateResponse` request model
- All `StreamingResponseEvent` types (8 event classes)
- Error handling with `ResponseFailedEvent`

✅ **FastAPI Integration**
- Auto-configured endpoints (`/health`, `/v1/entities`, `/v1/responses`)
- Agent discovery and routing
- SSE streaming response handling
- Error responses (404, 500)

✅ **Telemetry (Built-in, Mandatory)**
- OpenTelemetry SDK setup (TracerProvider, MeterProvider, LoggerProvider)
- OTLP exporter configuration
- FastAPI automatic instrumentation
- Logging integration with trace context
- Support for GenAI semantic conventions (no interference with child frameworks)
- Environment-based configuration (OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_SERVICE_NAME, etc.)
- Optional console exporter for local debugging

✅ **Developer Experience**
- Clear examples for common patterns
- Type hints throughout
- Helpful error messages
- Comprehensive docstrings

### Out of Scope (Future Phases)

❌ LLM framework integration helpers (developers pick their own)
❌ Custom structured output type helpers (framework-specific)
❌ Middleware/plugin system (can be added later)
❌ Agent composition/chaining (future phase)
❌ Authentication/authorization (assumed handled by gateway/infrastructure)
❌ Custom metric collectors (OTEL standard metrics are sufficient)

---

## Usage Examples

### Example 1: Simple Agent (No Framework)

```python
from agent_worker import Worker, WorkerAgent, EventStreamContext, extract_input
from agent_worker.protocol import CreateResponse, StreamingResponseEvent
from typing import AsyncIterator

class PigLatinAgent(WorkerAgent):
    name = "pig-latin"
    description = "Translates text to Pig Latin"
    
    async def execute(
        self, 
        request: CreateResponse, 
        response_id: str
    ) -> AsyncIterator[StreamingResponseEvent]:
        input_text = extract_input(request)
        result = self.translate_to_pig_latin(input_text)
        
        async with EventStreamContext(response_id) as stream:
            await stream.emit_text(result)
            async for event in stream. stream():
                yield event
    
    def translate_to_pig_latin(self, text: str) -> str:
        # ...   implementation
        pass

# Run with automatic telemetry
from agent_worker import setup_telemetry

worker = Worker([PigLatinAgent()])
setup_telemetry(worker.app, service_name="pig-latin-worker")
worker.run(port=5100)
```

**Lines of code:** 40 total (including telemetry setup)  
**Protocol boilerplate:** 0  
**Business logic:** 100%

### Example 2: With Pydantic AI + Telemetry

```python
from pydantic_ai import Agent
from agent_worker import Worker, WorkerAgent, EventStreamContext, extract_input
from agent_worker. protocol import CreateResponse, StreamingResponseEvent
from typing import AsyncIterator

class TravelPlannerAgent(WorkerAgent):
    name = "travel-planner"
    description = "Plans detailed travel itineraries"
    
    def __init__(self, llm_model):
        self.agent = Agent(
            model=llm_model,
            system_prompt="You are an expert travel planner..."
        )
    
    async def execute(
        self,
        request: CreateResponse,
        response_id: str
    ) -> AsyncIterator[StreamingResponseEvent]:
        input_text = extract_input(request)
        
        # Pydantic AI automatically emits GenAI semantic convention spans
        async with EventStreamContext(response_id) as stream:
            async for chunk in self.agent.run_stream(input_text):
                await stream.emit_text(chunk)
            
            async for event in stream. stream():
                yield event

# Run with telemetry (includes Pydantic AI GenAI spans automatically)
from agent_worker import Worker, setup_telemetry
from openai import AsyncOpenAI

client = AsyncOpenAI()
worker = Worker([
    TravelPlannerAgent(llm_model="gpt-4o")
])

# One-liner:  Full OTEL setup
setup_telemetry(worker.app, service_name="travel-planner-worker")
worker.run(port=5100)
```

**Trace output (in Aspire):**
- ✅ HTTP POST /v1/responses span
- ✅ agent.execute span
- ✅ invoke_agent (Pydantic AI span with GenAI attributes)
- ✅ llm.request + llm.response (with token counts, model name, etc.)

### Example 3: With Agent Framework

```python
from agent_framework import ChatAgent
from agent_worker import Worker, WorkerAgent, EventStreamContext, extract_input

class StoryWriterAgent(WorkerAgent):
    name = "story-writer"
    description = "Writes creative short stories"
    
    def __init__(self, chat_client):
        self.agent = ChatAgent(
            chat_client=chat_client,
            instructions="Write short, creative stories in 2-3 sentences"
        )
    
    async def execute(
        self,
        request: CreateResponse,
        response_id: str
    ) -> AsyncIterator[StreamingResponseEvent]:  
        input_text = extract_input(request)
        
        async with EventStreamContext(response_id) as stream:
            result = await self.agent.run(input_text)
            await stream.emit_text(result. text)
            
            async for event in stream.stream():
                yield event
```

---

## Integration with AgentGateway

### Discovery Flow

```
1. Gateway starts up
2. Gateway makes GET /v1/entities to Worker
   Response: 
   {
     "entities": [
       {
         "name": "pig-latin",
         "description": "Translates text to Pig Latin"
       },
       {
         "name":   "story-writer",
         "description":   "Writes creative short stories"
       }
     ]
   }
3. Gateway caches agent list and routes requests accordingly
```

### Execution Flow (with Telemetry)

```
1. Client requests:   POST /v1/responses to Gateway
   {
     "agent":   {"name": "story-writer"},
     "input":  "Write about cats"
   }

2. Gateway forwards to Worker:  POST /v1/responses
   (adds X-Response-ID header with unique ID)

3. Worker receives request (FastAPI span starts)
   → Routes to StoryWriterAgent. execute()
   → Agent uses framework (span created with GenAI semantics)
   → Streams SSE events

4. Agent streams events: 
   data:  {"type": "response. created", ...  }
   data: {"type": "response.in_progress", ... }
   data: {"type":  "response.output_item. added", ...}
   data: {"type": "response.output_text. delta", "delta": "Once...  "}
   data: {"type":  "response.output_text.delta", "delta": " upon... "}
   ...  
   data: {"type": "response.completed", ...}

5. Gateway caches and returns to client

6. Telemetry: Entire trace (HTTP → agent → LLM) visible in Aspire Dashboard
   with GenAI semantic conventions and token counts
```

### Configuration (Environment Variables)

```bash
# OTLP endpoint for trace/metric/log export
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# Service name in telemetry
OTEL_SERVICE_NAME=python-agent-worker

# Optional: Disable specific exporters
OTEL_TRACES_EXPORTER=otlp      # or "none" to disable
OTEL_METRICS_EXPORTER=otlp
OTEL_LOGS_EXPORTER=otlp

# For . NET integration
GATEWAY_URL=http://localhost:5390
```

---

## Integration Points with AgentWebChat Sample

### PythonAgent/pyproject.toml

```toml
[project]
name = "agent-webchat-python-worker"
version = "0.1.0"
description = "Python agent worker sample for AgentWebChat"
authors = [{name = "Microsoft"}]
requires-python = ">=3.10"
dependencies = [
    # Local path to the framework package
    "python-agent-worker @ file://../python-agent-worker",
    # Example agents use Pydantic AI
    "pydantic-ai>=0.7.0",
]
```

### PythonAgent/src/agents/main.py

```python
# Copyright (c) Microsoft.  All rights reserved. 

import os
from python_agent_worker import Worker, setup_telemetry
from .  pig_latin import PigLatinAgent
from . travel_itinerary import TravelPlannerAgent

def create_worker() -> Worker:
    return Worker([
        PigLatinAgent(),
        TravelPlannerAgent(),
    ])

if __name__ == "__main__": 
    worker = create_worker()
    
    # One-liner:  Full OTEL setup (traces, metrics, logs to Aspire)
    setup_telemetry(
        worker.app,
        service_name="python-agent-worker"
    )
    
    port = int(os.environ.get("AGENT_WORKER_PORT", 5100))
    worker.run(port=port)
```

### PythonAgent/src/agents/pig_latin.py

```python
# Copyright (c) Microsoft. All rights reserved.

from python_agent_worker import WorkerAgent, EventStreamContext, extract_input
from python_agent_worker.protocol import CreateResponse, StreamingResponseEvent
from typing import AsyncIterator

class PigLatinAgent(WorkerAgent):
    name = "pig-latin-agent"
    description = "Translates English text to Pig Latin"
    
    async def execute(
        self,
        request: CreateResponse,
        response_id: str
    ) -> AsyncIterator[StreamingResponseEvent]:
        input_text = extract_input(request)
        result = translate_to_pig_latin(input_text)
        
        async with EventStreamContext(response_id) as stream:
            await stream.emit_text(result)
            async for event in stream.stream():
                yield event

def translate_to_pig_latin(text: str) -> str:
    # ...   implementation (unchanged from current)
    pass
```

### PythonAgent/src/agents/travel_itinerary.py

```python
# Copyright (c) Microsoft. All rights reserved. 

from pydantic_ai import Agent
from python_agent_worker import WorkerAgent, EventStreamContext, extract_input
from python_agent_worker.protocol import CreateResponse, StreamingResponseEvent
from typing import AsyncIterator
import os

class TravelPlannerAgent(WorkerAgent):
    name = "travel-itinerary-agent"
    description = "Generates detailed travel itineraries"
    
    def __init__(self):
        # Pydantic AI automatically instruments with GenAI semantic conventions
        self.agent = Agent(
            model="openai: gpt-4o",
            system_prompt="You are an expert travel planner..."
        )
    
    async def execute(
        self,
        request:  CreateResponse,
        response_id: str
    ) -> AsyncIterator[StreamingResponseEvent]: 
        input_text = extract_input(request)
        
        # Pydantic AI spans automatically nested with GenAI semantics
        async with EventStreamContext(response_id) as stream:
            async for chunk in self. agent.run_stream(input_text):
                await stream.emit_text(chunk)
            
            async for event in stream.stream():
                yield event
```

### AgentWebChat. AppHost Integration (No Changes to Aspire Config)

```csharp
// dotnet/samples/AgentWebChat/AgentWebChat.AppHost/Program.cs

var pythonAgent = builder.AddUvicornApp(
    "python-agent",
    "../PythonAgent",
    "src. agents.main: app")  // Main FastAPI app from Worker
    .WithUv()
    .WithEndpoint("http", endpoint => endpoint. Port = 5100)
    // OTEL env vars for telemetry (standard)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")
    .WithEnvironment("OTEL_SERVICE_NAME", "python-agent-worker")
    .WaitFor(gateway);
```

---

## Boilerplate Reduction Metrics

| Metric | Before | After | Reduction |
|--------|--------|-------|-----------|
| Lines per agent | 60-100 | 15-20 | **75-80%** |
| Protocol code | Manual, error-prone | Zero | **100%** |
| Telemetry setup | Manual, optional | Automatic, mandatory | **N/A** |
| Testing friction | High (framework entanglement) | Low (pure logic) | **Significant** |
| Time to create agent | 2-3 hours | 15-30 minutes | **80%** |

---

## Success Criteria

### Quantitative

- **Boilerplate reduction:** 80%+ fewer lines of protocol code per agent
- **Telemetry coverage:** 100% of agent invocations traced and metered in Aspire
- **GenAI semantic conventions:** All Pydantic AI agents automatically report token counts, model names, and system info
- **Onboarding time:** New developers can build working agent in <15 minutes
- **Code coverage:** >90% test coverage
- **Performance:** <100ms overhead per request vs raw FastAPI
- **Adoption:** Used in 2+ production agent workers within 6 months

### Qualitative

- **Ease of use:** Developers focus on business logic, not framework concerns
- **Framework agnostic:** Works with any Python LLM framework
- **Observability:** Default telemetry provides visibility without effort
- **Maintainability:** Clear abstractions, well-documented
- **Community:** Positive feedback, feature requests for advanced use cases

---

## Implementation Plan

### Phase 1: MVP (Weeks 1-3)

**Week 1: Core Abstractions**
- [ ] Implement `WorkerAgent` ABC
- [ ] Implement `Worker` class with FastAPI setup
- [ ] Create endpoint handlers (`/health`, `/v1/entities`, `/v1/responses`)
- [ ] Add comprehensive type hints

**Week 2: Event Helper, Telemetry, & Protocol**
- [ ] Implement `EventStreamContext` with full lifecycle
- [ ] Implement `setup_telemetry()` function (OTEL SDK, OTLP, FastAPI instrumentation)
- [ ] Copy and adapt protocol models
- [ ] Implement `extract_input()` utility
- [ ] Add error handling (ResponseFailedEvent)
- [ ] Test Pydantic AI GenAI semantic conventions nesting

**Week 3: Polish, Examples, & Integration**
- [ ] Add docstrings and comprehensive examples
- [ ] Create example agents (simple, Pydantic AI, Agent Framework)
- [ ] Test end-to-end with AgentWebChat sample
- [ ] Verify telemetry in Aspire Dashboard
- [ ] Write README with quickstart
- [ ] Document telemetry setup and GenAI conventions

### Phase 2: Production Hardening (Weeks 4-5)

- [ ] Add comprehensive unit tests
- [ ] Add integration tests with mock gateway
- [ ] Add telemetry-specific tests (span creation, nesting, attributes)
- [ ] Test error scenarios and recovery
- [ ] Performance testing and optimization
- [ ] Security review
- [ ] Documentation completeness

### Phase 3: Documentation & Community (Weeks 6-8)

- [ ] Detailed API documentation
- [ ] Tutorial:   Building your first agent
- [ ] Tutorial:  Integrating with Pydantic AI with telemetry
- [ ] Tutorial:   Integrating with Agent Framework
- [ ] Migration guide:   From manual event streaming
- [ ] Telemetry troubleshooting guide
- [ ] GenAI semantic conventions reference
- [ ] Architecture documentation

---

## Open Questions & Decisions

### Q1: PyPI Publishing Timeline

**Current Decision:** Phase 1 as local package, publish after stable.   
**Rationale:** Allows rapid iteration with AgentWebChat sample.  
**Future:** Publish to PyPI once API is stable (v1.0+)

### Q2: GenAI Semantic Conventions Extensibility

**Current Decision:** Support automatic nesting for child frameworks; no custom attributes.  
**Rationale:** OTEL semantic conventions are standardized; no need for custom extensions.  
**Future:** If new standards emerge, update SDKs accordingly. 

### Q3: Custom Span Creation in Agents

**Current Decision:** Provide `get_tracer()` helper for advanced users.  
**Rationale:** Developers can create custom spans if needed, but not required.  
**Example:**
```python
from python_agent_worker. telemetry import get_tracer

tracer = get_tracer(__name__)
with tracer.start_as_current_span("my_custom_operation") as span:
    span.set_attribute("custom. field", "value")
    # ... do work
```

---

## References

- [Agent Gateway Source](https://github.com/luisquintanilla/agent-drt/tree/feature/python-agent-integration/dotnet/samples/AgentWebChat/AgentGateway)
- [OpenAI Responses API](https://platform.openai.com/docs/api-reference/responses)
- [OpenTelemetry Documentation](https://opentelemetry.io/docs/)
- [OpenTelemetry GenAI Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/)
- [Pydantic AI Documentation](https://ai.pydantic.dev/)
- [Agent Framework Documentation](https://learn.microsoft.com/agent-framework/)
- [FastAPI Documentation](https://fastapi.tiangolo.com/)

---

## Appendix: Migration Path for Existing Agents

### Current PythonAgent → python-agent-worker

**Before:**

```
main.py (87 lines)
models.py (108 lines)
streaming.py (18 lines)
telemetry.py (91 lines)
agents/pig_latin.py (108 lines:  50 boilerplate + 58 logic)
agents/travel_itinerary.py (118 lines: 50 boilerplate + 68 logic)
Total: 530 lines
```

**After:**

```
pyproject.toml (5 lines:  add python-agent-worker dependency)
src/agents/main.py (10 lines: Worker + setup_telemetry)
src/agents/pig_latin.py (20 lines: pure logic)
src/agents/travel_itinerary.py (25 lines: pure logic)
Total: 60 lines
Reduction: 88. 7%
```

### Migration Steps

1. Add `python-agent-worker` to `pyproject.toml`
2. Move agent logic to new file structure
3. Wrap logic in `WorkerAgent` subclasses
4. Use `EventStreamContext` instead of manual event creation
5. Replace telemetry setup with one-line `setup_telemetry()`
6. Delete all protocol/streaming boilerplate files
7. Test with AgentGateway (endpoints haven't changed)

**Estimated migration time:** 30 minutes per worker

---

**Document Version:** 2.0 (Updated with OTEL telemetry as default, GenAI semantic conventions, and separate directory structure)  
**Last Updated:** 2025-12-24  
**Next Review:** After Phase 1 completion