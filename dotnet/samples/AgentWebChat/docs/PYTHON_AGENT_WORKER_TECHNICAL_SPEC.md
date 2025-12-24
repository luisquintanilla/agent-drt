# Python Agent Worker Framework - Technical Specification

**Document Status:** Draft  
**Date:** December 24, 2025  
**Audience:** Engineering team (implementation)  
**Related Documents:** PRD, API Reference

---

## 1. Architecture Overview

### 1.1 High-Level Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                   Aspire / OTEL Collector                    │
└────────────────────────────────────┬─────────────────────────┘
                                     │ (OTLP)
                    ┌────────────────┴─────────────────┐
                    │                                  │
                    ▼                                  ▼
        ┌──────────────────────┐         ┌──────────────────────┐
        │   OTLPSpanExporter   │         │ OTLPMetricExporter   │
        └──────────────────────┘         └──────────────────────┘
                    │                                  │
                    └────────────────┬─────────────────┘
                                     │
                    ┌────────────────▼─────────────────┐
                    │                                  │
                    │   OpenTelemetry SDK              │
                    │  ├─ TracerProvider               │
                    │  ├─ MeterProvider                │
                    │  ├─ LoggerProvider               │
                    │  └─ Resource (service metadata)  │
                    │                                  │
                    └────────────────┬─────────────────┘
                                     │
        ┌────────────────────────────▼────────────────────────────┐
        │                                                          │
        │  FastAPI Application                                    │
        │  ├─ GET /health (auto-instrumented)                    │
        │  ├─ GET /v1/entities (auto-instrumented)               │
        │  └─ POST /v1/responses (auto-instrumented)             │
        │                                                          │
        │     ├─ Request validation (Pydantic)                   │
        │     ├─ Agent lookup (dict)                             │
        │     └─ EventStreamContext                              │
        │         ├─ emit_text()                                 │
        │         ├─ emit_structured()                           │
        │         └─ Automatic events                            │
        │                                                          │
        │             ▼                                           │
        │         Worker Agent. execute()                          │
        │         (Developer code)                                │
        │             │                                           │
        │             ├─ Pydantic AI (auto-instrumented)         │
        │             │  └─ GenAI semantic conventions           │
        │             ├─ Agent Framework (if instrumented)       │
        │             └─ Custom code                             │
        │                                                          │
        │             ▼                                           │
        │         Spans nested:  HTTP → Agent → LLM               │
        │                                                          │
        │  (FastAPIInstrumentor + LoggingInstrumentor active)     │
        │                                                          │
        └────────────────────────────────────────────────────────┘
                                     │
                    ┌────────────────▼─────────────────┐
                    │   AgentGateway (Consumer)        │
                    │   (Makes HTTP requests)          │
                    └────────────────────────────────┘
```

---

## 2. Component Specifications

### 2.1 WorkerAgent (Abstract Base Class)

**File:** `agent_worker/agent.py`

```python
"""
WorkerAgent:   Abstract base class for gateway-compatible agents. 

Developers subclass this and implement the execute() method.
The framework handles all protocol, streaming, and telemetry concerns.
"""

from abc import ABC, abstractmethod
from typing import AsyncIterator
from . protocol.models import CreateResponse, StreamingResponseEvent


class WorkerAgent(ABC):
    """
    Abstract base for agents compatible with AgentGateway.
    
    Attributes:
        name (str): Unique agent identifier (alphanumeric + underscore, max 128 chars)
        description (str): Human-readable description (max 256 chars)
    
    Subclasses must implement:
        execute(): Async method yielding StreamingResponseEvent objects
    
    Example:
        class MyAgent(WorkerAgent):
            name = "my-agent"
            description = "Does something useful"
            
            async def execute(self, request, response_id):
                async with EventStreamContext(response_id) as stream:
                    result = await my_logic(extract_input(request))
                    await stream.emit_text(result)
                    async for event in stream.stream():
                        yield event
    """
    
    name: str = None
    description: str = None
    
    def __init__(self):
        """
        Initialize agent. 
        
        Raises:
            TypeError: If name or description not set by subclass
        """
        if not self.name:
            raise TypeError(f"{self.__class__.__name__} must set 'name' attribute")
        if not self.description:
            raise TypeError(f"{self.__class__.__name__} must set 'description' attribute")
        
        # Validate name format
        if not self._validate_name(self.name):
            raise ValueError(
                f"Agent name '{self. name}' invalid.  "
                f"Must match [a-zA-Z0-9_-]{{1,128}}"
            )
    
    @staticmethod
    def _validate_name(name: str) -> bool:
        """Validate agent name format."""
        import re
        return bool(re. match(r'^[a-zA-Z0-9_-]{1,128}$', name))
    
    @abstractmethod
    async def execute(
        self, 
        request: CreateResponse,
        response_id: str,
    ) -> AsyncIterator[StreamingResponseEvent]: 
        """
        Execute the agent and stream response events.
        
        Args:
            request: Gateway request containing input, instructions, metadata
            response_id: Unique ID for this response (from X-Response-ID header or generated)
        
        Yields:
            StreamingResponseEvent objects in OpenAI Responses API format. 
            The EventStreamContext helper manages most of these automatically.
        
        Raises:
            Any exception is caught by EventStreamContext and converted to
            ResponseFailedEvent.  Developers can also catch exceptions manually.
        
        Implementation Notes:
            - Use extract_input(request) to parse input from various formats
            - Wrap execution in:  async with EventStreamContext(response_id) as stream
            - Call stream.emit_text() for text chunks
            - Call stream.emit_structured(dict) for JSON data
            - Yield all events:  async for event in stream.stream(): yield event
            - Framework auto-instruments with OpenTelemetry (no manual spans needed)
        """
        ... 
```

---

### 2.2 Worker (FastAPI Orchestrator)

**File:** `agent_worker/worker.py`

```python
"""
Worker: FastAPI orchestrator for multiple agents.

Handles: 
- FastAPI app creation and routing
- Agent registration and discovery  
- Request validation and routing
- Response streaming
- Error handling and logging
"""

from typing import List, Dict, Optional
from fastapi import FastAPI, Request, status
from fastapi.responses import JSONResponse
from sse_starlette. sse import EventSourceResponse
import logging
import uuid
import re

from .agent import WorkerAgent
from .protocol.models import CreateResponse, StreamingResponseEvent

logger = logging.getLogger(__name__)


class Worker: 
    """
    Manages FastAPI application and agent lifecycle.
    
    Handles all protocol and infrastructure concerns.
    One-liner initialization for developers.
    
    Usage:
        worker = Worker([Agent1(), Agent2()])
        worker.run(port=8000)
    
    Environment variables:
        AGENT_WORKER_PORT: Port to run on (default: 8000)
    """
    
    def __init__(self, agents: List[WorkerAgent]):
        """
        Initialize worker with agents.
        
        Args:
            agents: List of WorkerAgent instances
        
        Raises:
            ValueError: If agents list empty, duplicate names, or invalid names
        """
        if not agents:
            raise ValueError("Must provide at least one agent")
        
        # Validate and register agents
        self.agents: Dict[str, WorkerAgent] = {}
        for agent in agents: 
            # Validation happens in WorkerAgent.__init__
            if agent.name in self.agents:
                raise ValueError(f"Duplicate agent name: {agent.name}")
            self.agents[agent.name] = agent
        
        # Create FastAPI app with all endpoints
        self.app = self._create_app()
        
        logger.info(
            f"Worker initialized with {len(self.agents)} agent(s): "
            f"{', '.join(self.agents.keys())}"
        )
    
    def _create_app(self) -> FastAPI:
        """
        Create and configure FastAPI application.
        
        Registers endpoints: 
        - GET /health - Health check for gateway probing
        - GET /v1/entities - Agent discovery (AgentGateway discovers capabilities)
        - POST /v1/responses - Agent execution (AgentGateway routes requests here)
        
        Returns: 
            Configured FastAPI application
        """
        app = FastAPI(
            title="Agent Worker",
            description="Python agent worker for AgentGateway",
            version="0.1.0",
        )
        
        # ============================================================
        # GET /health - Health check
        # ============================================================
        @app.get("/health")
        async def health_check():
            """
            Health check endpoint. 
            
            Gateway's WorkerHealthCheckService polls this to determine
            if the worker is available. 
            
            Returns:
                {"status": "healthy", "agents": N}
            """
            logger.debug("Health check endpoint called")
            return {
                "status": "healthy",
                "agents": len(self.agents)
            }
        
        # ============================================================
        # GET /v1/entities - Agent discovery
        # ============================================================
        @app.get("/v1/entities")
        async def discover_agents():
            """
            Agent discovery endpoint.
            
            Gateway calls this to discover which agents this worker supports.
            Response format matches OpenAI API agent discovery.
            
            Returns:
                {
                  "entities": [
                    {"name": "agent1", "description": "... "},
                    {"name": "agent2", "description": "... "}
                  ]
                }
            """
            logger.info(f"Agent discovery called - returning {len(self.agents)} agents")
            return {
                "entities": [
                    {
                        "name": agent. name,
                        "description":  agent.description,
                    }
                    for agent in self.agents.values()
                ]
            }
        
        # ============================================================
        # POST /v1/responses - Agent execution
        # ============================================================
        @app.post("/v1/responses")
        async def execute_agent(request_body: dict, http_request: Request):
            """
            Execute an agent and stream response. 
            
            Gateway sends CreateResponse request in OpenAI Responses API format.
            Worker routes to correct agent and streams SSE events.
            
            Request body (CreateResponse):
                {
                  "agent":  {"name": "agent-name"},  # or "model": "agent-name"
                  "input": "user input or message array",
                  "instructions": "additional instructions",
                  "metadata":  {"key": "value"},
                  "stream":  true,
                  "conversation_id": "optional"
                }
            
            Request headers:
                X-Response-ID:  Unique response ID (optional, generated if missing)
            
            Response:
                Server-Sent Events (SSE) stream:
                data: {"type": "response. created", ... }
                data: {"type": "response.in_progress", ...}
                data: {"type": "response.output_item. added", ...}
                data: {"type": "response.output_text. delta", "delta": "text..."}
                ... 
                data: {"type": "response.completed", ...}
            
            Error responses:
                400: Invalid request format
                404: Agent not found  
                500: Execution error
            """
            
            # Step 1: Parse and validate request
            try:
                request = CreateResponse(**request_body)
            except Exception as e:
                logger.error(f"Invalid request format: {e}")
                return JSONResponse(
                    status_code=status.HTTP_400_BAD_REQUEST,
                    content={
                        "error": {
                            "message": f"Invalid request:  {str(e)}",
                            "type": "invalid_request_error",
                        }
                    },
                )
            
            # Step 2: Extract agent name from various fields
            agent_name = None
            if request.agent and request.agent.name:
                agent_name = request.agent.name
            elif request.model:
                agent_name = request.model
            elif request.metadata and "entity_id" in request.metadata:
                agent_name = request.metadata["entity_id"]
            
            if not agent_name:
                logger.error("No agent name specified in request")
                return JSONResponse(
                    status_code=status.HTTP_400_BAD_REQUEST,
                    content={
                        "error": {
                            "message": "Agent name required (via agent.name, model, or metadata. entity_id)",
                            "type": "invalid_request_error",
                        }
                    },
                )
            
            # Step 3: Lookup agent
            if agent_name not in self.agents:
                logger.error(f"Agent not found: {agent_name}")
                return JSONResponse(
                    status_code=status.HTTP_404_NOT_FOUND,
                    content={
                        "error": {
                            "message": f"Agent '{agent_name}' not found",
                            "type": "invalid_request_error",
                        },
                        "available_agents": list(self.agents.keys()),
                    },
                )
            
            # Step 4: Get or generate response ID
            response_id = (
                http_request.headers.get("X-Response-ID") 
                or f"resp_{uuid.uuid4().hex}"
            )
            
            logger.info(
                f"Executing agent '{agent_name}' "
                f"(response_id={response_id})"
            )
            
            # Step 5: Execute agent and stream events
            try:
                agent = self.agents[agent_name]
                
                # Agent. execute() returns AsyncIterator[StreamingResponseEvent]
                event_stream = agent.execute(request, response_id)
                
                # Convert to SSE format and return
                sse_stream = self._stream_events(event_stream)
                return EventSourceResponse(sse_stream)
            
            except Exception as e: 
                logger.exception(f"Error executing agent '{agent_name}'")
                return JSONResponse(
                    status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                    content={
                        "error": {
                            "message": f"Agent execution failed: {str(e)}",
                            "type": "internal_error",
                        }
                    },
                )
        
        # ============================================================
        # Global error handler
        # ============================================================
        @app.exception_handler(Exception)
        async def exception_handler(request: Request, exc: Exception):
            """Catch-all for unhandled exceptions."""
            logger.exception(f"Unhandled exception:  {exc}")
            return JSONResponse(
                status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                content={
                    "error": {
                        "message": "Internal server error",
                        "type":  "internal_error",
                    }
                },
            )
        
        return app
    
    @staticmethod
    async def _stream_events(
        event_stream: AsyncIterator[StreamingResponseEvent],
    ) -> AsyncIterator[str]:
        """
        Convert StreamingResponseEvent objects to SSE format.
        
        EventSourceResponse expects:
        - Async generator yielding strings
        - Each string is sent as "data: {string}\n\n"
        
        Args:
            event_stream: AsyncIterator of StreamingResponseEvent
        
        Yields:
            JSON strings (one per event)
        """
        try:
            async for event in event_stream:
                # Serialize event to JSON (exclude None values)
                json_str = event.model_dump_json(exclude_none=True)
                yield json_str
                logger.debug(f"Streamed event: {event. type}")
        except Exception as e: 
            logger.exception(f"Error streaming events: {e}")
            # SSE stream has already started, can't send error response
            # Best effort:  log and close stream
            raise
    
    def run(
        self,
        host: str = "0.0.0.0",
        port: int = 8000,
        reload: bool = False,
        log_level: str = "info",
    ):
        """
        Run the FastAPI server.
        
        Args:
            host: Host to bind to (default: 0.0.0.0)
            port: Port to bind to (default: 8000, override with AGENT_WORKER_PORT env var)
            reload: Auto-reload on code changes (default: False, use in dev)
            log_level:  Logging level (default: info)
        
        Example:
            worker = Worker([MyAgent()])
            worker.run(port=5100, log_level="debug")
        """
        import uvicorn
        
        logger.info(
            f"Starting agent worker on {host}:{port} "
            f"with {len(self.agents)} agent(s)"
        )
        
        uvicorn.run(
            self.app,
            host=host,
            port=port,
            reload=reload,
            log_level=log_level,
        )
```

---

### 2.3 EventStreamContext (Boilerplate Helper)

**File:** `agent_worker/helpers. py`

```python
"""
EventStreamContext: Context manager for event streaming. 

Eliminates boilerplate by automatically managing event lifecycle. 
Developers just call emit_text() or emit_structured().
"""

from contextlib import asynccontextmanager
from typing import AsyncIterator, Optional, Dict, Any
import uuid
import time
import json
import logging

from . protocol.models import (
    StreamingResponseEvent,
    ResponseStatus,
    ItemResource,
    ItemContent,
    ResponseCreatedEvent,
    ResponseInProgressEvent,
    OutputItemAddedEvent,
    OutputTextDeltaEvent,
    OutputTextDoneEvent,
    ResponseCompletedEvent,
    ResponseFailedEvent,
)

logger = logging.getLogger(__name__)


class EventStreamContext:
    """
    Context manager for streaming agent responses.
    
    Automatically handles event creation, sequencing, and lifecycle.
    Developers just emit text or structured data.
    
    Usage:
        async with EventStreamContext(response_id) as stream:
            # Your agent logic
            result = await my_agent.run(input)
            
            # Emit text (optional chunking)
            await stream.emit_text(result)
            
            # Emit structured data (optional)
            await stream.emit_structured({"key": "value"})
            
            # Yield all events
            async for event in stream.stream():
                yield event
    
    Events emitted automatically:
        - __aenter__:   
          1. ResponseCreatedEvent (response. created)
          2. ResponseInProgressEvent (response.in_progress)
          3. OutputItemAddedEvent (response.output_item.added)
        
        - emit_text():
          * OutputTextDeltaEvent (response.output_text.delta) per chunk
        
        - __aexit__ (success):
          1. OutputTextDoneEvent (response.output_text.done)
          2. ResponseCompletedEvent (response.completed)
        
        - __aexit__ (error):
          1. ResponseFailedEvent (response.failed)
    """
    
    def __init__(self, response_id: str, item_id: Optional[str] = None):
        """
        Initialize stream context.
        
        Args:
            response_id: Unique response identifier (from gateway or generated)
            item_id:  Optional output item ID.  If not provided, generates one.
        
        Raises:
            TypeError: If response_id is not a string
        """
        if not isinstance(response_id, str):
            raise TypeError(f"response_id must be str, got {type(response_id)}")
        
        self.response_id = response_id
        self.item_id = item_id or f"item_{uuid.uuid4().hex}"
        self.sequence = 0
        self.all_text = ""
        self.start_time = time.time()
        self._events: list[StreamingResponseEvent] = []
        self._entered = False
        
        logger.debug(
            f"EventStreamContext created "
            f"(response_id={response_id}, item_id={self.item_id})"
        )
    
    async def emit_text(self, text: str) -> None:
        """
        Emit a chunk of text.
        
        Creates OutputTextDeltaEvent automatically.
        Text is accumulated for final OutputTextDoneEvent.
        
        Args:
            text: Text chunk to emit
        
        Raises:
            RuntimeError: If called outside context manager (before __aenter__)
            TypeError: If text is not a string
        """
        if not self._entered:
            raise RuntimeError(
                "emit_text() can only be called inside 'async with' block"
            )
        
        if not isinstance(text, str):
            raise TypeError(f"text must be str, got {type(text)}")
        
        self.all_text += text
        
        event = OutputTextDeltaEvent(
            sequence_number=self.sequence,
            item_id=self.item_id,
            delta=text,
            output_index=0,
            content_index=0,
        )
        self. sequence += 1
        self._events.append(event)
        
        logger.debug(
            f"Emitted text delta: {len(text)} chars "
            f"(total: {len(self.all_text)} chars)"
        )
    
    async def emit_structured(self, data: Dict[str, Any]) -> None:
        """
        Emit structured data as JSON.
        
        Serializes dict to JSON and emits as text. 
        Useful for structured outputs (itineraries, decisions, etc.).
        
        Args:
            data: Dictionary to serialize as JSON
        
        Raises: 
            TypeError: If data is not a dict
            ValueError: If data is not JSON-serializable
        
        Example:
            await stream. emit_structured({
                "location": "Paris",
                "attractions": ["Eiffel Tower", "Louvre"],
                "days": 3
            })
        """
        if not isinstance(data, dict):
            raise TypeError(f"data must be dict, got {type(data)}")
        
        try:
            json_str = json.dumps(data)
        except TypeError as e:
            raise ValueError(f"Data not JSON-serializable: {e}")
        
        await self.emit_text(json_str)
        logger.debug(f"Emitted structured data: {len(json_str)} bytes")
    
    async def __aenter__(self):
        """
        Enter context:  emit initialization events.
        
        Emits in order:
        1. ResponseCreatedEvent (response.created)
           - Marks that response generation has started
        2. ResponseInProgressEvent (response.in_progress)
           - Marks that processing is underway
        3. OutputItemAddedEvent (response.output_item.added)
           - Creates the output item that will receive text
        
        Returns:
            self (for 'async with ...  as stream:' pattern)
        """
        self._entered = True
        created_at = int(self.start_time)
        
        logger.debug(
            f"EventStreamContext entered "
            f"(response_id={self.response_id})"
        )
        
        # 1. ResponseCreatedEvent
        response_status = ResponseStatus(
            id=self.response_id,
            status="in_progress",
            created_at=created_at,
        )
        created_event = ResponseCreatedEvent(
            sequence_number=self. sequence,
            response=response_status,
        )
        self.sequence += 1
        self._events.append(created_event)
        logger.debug("Emitted ResponseCreatedEvent")
        
        # 2. ResponseInProgressEvent
        in_progress_event = ResponseInProgressEvent(
            sequence_number=self.sequence,
            response=response_status,
        )
        self.sequence += 1
        self._events.append(in_progress_event)
        logger.debug("Emitted ResponseInProgressEvent")
        
        # 3. OutputItemAddedEvent
        item = ItemResource(
            id=self.item_id,
            type="message",
            object="response. item",
            role="assistant",
            content=[ItemContent(type="text", text="")],
        )
        item_added_event = OutputItemAddedEvent(
            sequence_number=self.sequence,
            item=item,
            output_index=0,
        )
        self.sequence += 1
        self._events.append(item_added_event)
        logger.debug(f"Emitted OutputItemAddedEvent (item_id={self.item_id})")
        
        return self
    
    async def __aexit__(self, exc_type, exc_val, exc_tb):
        """
        Exit context: emit completion or failure events.
        
        Success path (exc_type is None):
        1. OutputTextDoneEvent (response.output_text.done)
           - Marks that text generation is complete
        2. ResponseCompletedEvent (response.completed)
           - Marks that response is fully generated
        
        Error path (exc_type is not None):
        1. ResponseFailedEvent (response.failed)
           - Marks that response generation failed
           - Includes error message and exception type
        
        Args:
            exc_type: Exception type (if any exception occurred)
            exc_val:  Exception value (if any)
            exc_tb: Exception traceback (if any)
        
        Returns:
            False (allows exceptions to propagate)
            True (suppresses exceptions - we don't use this)
        
        Note:
            Logs the exception at ERROR level if one occurred.
            Emits ResponseFailedEvent with error details.
        """
        
        if exc_type is not None:
            # Error path:  Emit failure event
            error_msg = str(exc_val) if exc_val else str(exc_type)
            error_type = exc_type.__name__ if exc_type else "unknown"
            
            failed_event = ResponseFailedEvent(
                sequence_number=self. sequence,
                response=ResponseStatus(
                    id=self.response_id,
                    status="failed",
                    created_at=int(self.start_time),
                ),
                error={
                    "message": error_msg,
                    "type":  error_type,
                },
            )
            self.sequence += 1
            self._events. append(failed_event)
            
            logger.error(
                f"Agent execution failed: {error_type}:  {error_msg}",
                exc_info=(exc_type, exc_val, exc_tb),
            )
            
            # Return False to propagate exception (let caller handle it)
            return False
        
        # Success path: Emit completion events
        logger.debug("Agent execution completed successfully")
        
        # 1. OutputTextDoneEvent
        text_done_event = OutputTextDoneEvent(
            sequence_number=self.sequence,
            item_id=self.item_id,
            text=self.all_text,
            output_index=0,
            content_index=0,
        )
        self.sequence += 1
        self._events.append(text_done_event)
        logger.debug(f"Emitted OutputTextDoneEvent ({len(self.all_text)} chars)")
        
        # 2. ResponseCompletedEvent
        duration = time.time() - self.start_time
        completed_event = ResponseCompletedEvent(
            sequence_number=self.sequence,
            response=ResponseStatus(
                id=self.response_id,
                status="completed",
                created_at=int(self.start_time),
            ),
        )
        self.sequence += 1
        self._events.append(completed_event)
        logger.debug(f"Emitted ResponseCompletedEvent (duration={duration:.2f}s)")
    
    async def stream(self) -> AsyncIterator[StreamingResponseEvent]:
        """
        Iterate accumulated events for streaming.
        
        Should be called after context exits to ensure all events
        (including completion events) are included.
        
        Yields all events in sequence order.
        
        Yields:
            StreamingResponseEvent objects
        
        Raises:
            RuntimeError: If called before context entered
        
        Example:
            async with EventStreamContext(response_id) as stream:
                await stream.emit_text("hello")
                async for event in stream.stream():
                    yield event  # Send to client
        """
        if not self._entered:
            logger.warning("stream() called before context entered")
        
        for event in self._events:
            yield event


def extract_input(request) -> str:
    """
    Extract text input from gateway request.
    
    The gateway sends input in multiple possible formats:
    
    1. Simple string: 
        "hello world"
    
    2. Array of message objects:
        [
          {"role": "user", "content":  "hello"}
        ]
    
    3. Array with complex content:
        [
          {"role": "user", "content": [{"type": "text", "text":  "hello"}]}
        ]
    
    Args:
        request: CreateResponse object from gateway
    
    Returns:
        Extracted text string (empty string if extraction fails)
    
    Raises:
        None (fails gracefully, logs warning, returns "")
    
    Example:
        input_text = extract_input(request)
        result = await my_logic(input_text)
    """
    try:
        # Case 1: Simple string input
        if isinstance(request.input, str):
            logger.debug(f"Extracted simple string input: {len(request.input)} chars")
            return request.input
        
        # Case 2: Array of message objects
        if isinstance(request.input, list) and len(request.input) > 0:
            logger.debug(f"Extracting from message array: {len(request.input)} items")
            
            # Find last user message (most recent)
            for item in reversed(request.input):
                if not isinstance(item, dict):
                    continue
                
                if item.get("role") != "user":
                    continue
                
                content = item.get("content")
                
                # Case 2a: Simple string content
                if isinstance(content, str):
                    logger.debug(
                        f"Extracted content from user message (string): "
                        f"{len(content)} chars"
                    )
                    return content
                
                # Case 2b: Complex content array
                if isinstance(content, list):
                    for content_item in content:
                        if isinstance(content_item, dict):
                            if content_item. get("type") in ["text", "input_text"]:
                                text = content_item.get("text")
                                if isinstance(text, str):
                                    logger.debug(
                                        f"Extracted content from user message (complex): "
                                        f"{len(text)} chars"
                                    )
                                    return text
        
        # Fallback: could not extract
        logger.warning(
            f"Could not extract input from request.  "
            f"Input type: {type(request.input)}"
        )
        return ""
    
    except Exception as e: 
        logger.error(f"Error extracting input:  {e}", exc_info=True)
        return ""
```

---

### 2.4 OpenTelemetry Telemetry Module

**File:** `agent_worker/telemetry.py`

```python
"""
Telemetry Setup:  OpenTelemetry integration for Agent Worker.

Configures: 
1.  Tracing (FastAPI, agent execution, child library spans like Pydantic AI)
2. Metrics (request counts, latencies, token usage)
3. Logging (structured logs with trace context)
4. GenAI semantic conventions (automatic nesting of Pydantic AI spans)

Environment Variables (standard OTEL):
    OTEL_EXPORTER_OTLP_ENDPOINT: OTLP collector endpoint (default: http://localhost:4317)
    OTEL_SERVICE_NAME: Service name (default: agent-worker)
    OTEL_TRACES_EXPORTER: Tracer exporter (default: otlp)
    OTEL_METRICS_EXPORTER:  Metrics exporter (default: otlp)
    OTEL_LOGS_EXPORTER:  Logs exporter (default: otlp)

Usage:
    from python_agent_worker import Worker, setup_telemetry
    
    worker = Worker([MyAgent()])
    setup_telemetry(worker. app, service_name="my-worker")
    worker.run()
"""

import os
import logging
from typing import Optional
from fastapi import FastAPI

from opentelemetry import trace, metrics, logs
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk._logs import LoggerProvider, LoggingHandler
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.exporter.otlp.proto. grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.exporter.otlp.proto. grpc.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.grpc._log_exporter import OTLPLogExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.logging import LoggingInstrumentor

logger = logging.getLogger(__name__)


def setup_telemetry(
    app: FastAPI,
    service_name: str = "agent-worker",
    otlp_endpoint: Optional[str] = None,
    enable_console_exporter: bool = False,
) -> None:
    """
    Configure OpenTelemetry for the agent worker.
    
    Sets up tracing, metrics, and logging with OTLP export.
    FastAPI automatically instrumented. 
    Child libraries (Pydantic AI, etc.) auto-instrument with GenAI semantics.
    
    Args:
        app: FastAPI application instance to instrument
        service_name: Service name for telemetry (default: "agent-worker")
        otlp_endpoint: OTLP collector endpoint.  If None, uses: 
            1.  OTEL_EXPORTER_OTLP_ENDPOINT environment variable
            2. Default:  http://localhost:4317
        enable_console_exporter: Also export to console for debugging (default: False)
    
    Environment Variables (standard OTEL):
        OTEL_EXPORTER_OTLP_ENDPOINT:  OTLP endpoint (overrides otlp_endpoint param)
        OTEL_SERVICE_NAME: Service name (overrides service_name param)
        OTEL_TRACES_EXPORTER: Tracer exporter (default: otlp, "none" to disable)
        OTEL_METRICS_EXPORTER: Metrics exporter (default:  otlp, "none" to disable)
        OTEL_LOGS_EXPORTER:  Logs exporter (default: otlp, "none" to disable)
    
    Example:
        from python_agent_worker import Worker, setup_telemetry
        
        worker = Worker([
            TravelPlannerAgent(),
            PigLatinAgent(),
        ])
        
        # One-liner for full OTEL setup
        setup_telemetry(
            worker.app,
            service_name="agent-webchat-python-worker",
            otlp_endpoint="http://aspire-dashboard:4317"
        )
        
        worker.run(port=5100)
    
    How it works with Pydantic AI:
        
        1. When you use Pydantic AI (Agent. run_stream()):
           - Pydantic AI automatically creates spans: 
             * "invoke_agent {name}" with GenAI semantic attributes
             * "llm. request" for each LLM call
             * "llm.response" with token counts, model, etc.
        
        2. These spans are automatically NESTED under: 
           - FastAPI span for "POST /v1/responses"
           - Which is under the overall trace
        
        3. Attributes are automatically populated:
           - gen_ai. system = "pydantic-ai"
           - gen_ai.request. model = "gpt-4o"
           - gen_ai.usage.input_tokens = 150
           - gen_ai.usage.output_tokens = 280
        
        4. All visible in Aspire Dashboard or OTEL-compatible backend
        
        5. No developer configuration needed - automatic! 
    """
    
    # Get configuration from environment or parameters
    otlp_endpoint = otlp_endpoint or os. getenv(
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "http://localhost:4317"
    )
    service_name = os.getenv("OTEL_SERVICE_NAME", service_name)
    
    logger.info(
        f"Configuring OpenTelemetry for service: {service_name} "
        f"(OTLP endpoint: {otlp_endpoint})"
    )
    
    # ============================================================
    # 1. TRACING - Spans for requests, agent execution, LLM calls
    # ============================================================
    
    # Create resource (service metadata)
    resource = Resource. create({
        "service.name": service_name,
        "service.version": "0.1.0",
    })
    
    # Create TracerProvider
    trace_provider = TracerProvider(resource=resource)
    
    # Add OTLP exporter (sends to Aspire/Jaeger/etc)
    trace_provider.add_span_processor(
        BatchSpanProcessor(
            OTLPSpanExporter(endpoint=otlp_endpoint)
        )
    )
    
    # Optional: Console exporter for local debugging
    if enable_console_exporter:
        from opentelemetry.sdk.trace. export import ConsoleSpanExporter
        trace_provider.add_span_processor(
            BatchSpanProcessor(ConsoleSpanExporter())
        )
    
    trace.set_tracer_provider(trace_provider)
    logger.debug("Configured TracerProvider with OTLP exporter")
    
    # ============================================================
    # 2. METRICS - Request counts, latencies, token usage
    # ============================================================
    
    metric_reader = PeriodicExportingMetricReader(
        OTLPMetricExporter(endpoint=otlp_endpoint)
    )
    meter_provider = MeterProvider(
        resource=resource,
        metric_readers=[metric_reader]
    )
    metrics.set_meter_provider(meter_provider)
    logger.debug("Configured MeterProvider with OTLP exporter")
    
    # ============================================================
    # 3. LOGGING - Structured logs with trace context
    # ============================================================
    
    logger_provider = LoggerProvider(resource=resource)
    logger_provider.add_log_record_processor(
        BatchLogRecordProcessor(
            OTLPLogExporter(endpoint=otlp_endpoint)
        )
    )
    logs.set_logger_provider(logger_provider)
    
    # Add logging handler to propagate logs with trace context
    handler = LoggingHandler(
        level=logging. NOTSET,
        logger_provider=logger_provider
    )
    logging.getLogger().addHandler(handler)
    logger.debug("Configured LoggerProvider with OTLP exporter")
    
    # ============================================================
    # 4. INSTRUMENTATION - Auto-instrument FastAPI and logging
    # ============================================================
    
    # FastAPI automatic instrumentation
    # Creates spans for:
    # - HTTP requests (GET /health, GET /v1/entities, POST /v1/responses)
    # - Request/response processing
    # - Errors
    FastAPIInstrumentor.instrument_app(app)
    logger.debug("Instrumented FastAPI application")
    
    # Logging instrumentation
    # Adds trace context (trace_id, span_id) to all logs
    LoggingInstrumentor().instrument()
    logger.debug("Instrumented Python logging")
    
    # ============================================================
    # 5. CHILD LIBRARY INSTRUMENTATION (Pydantic AI, etc.)
    # ============================================================
    #
    # Pydantic AI automatically instruments itself when:
    # - opentelemetry-api is installed (yes, as dependency)
    # - opentelemetry-sdk is installed (yes, as dependency)
    #
    # It creates spans like:
    # - "invoke_agent {agent_name}"
    #   Attributes: 
    #   - gen_ai.system = "pydantic-ai"
    #   - gen_ai.operation. name = "invoke_agent"
    # 
    # - "llm. request" for each LLM call
    #   Attributes: 
    #   - gen_ai. request.model = model name
    #   - gen_ai.request.max_tokens = token limit
    # 
    # - "llm.response" for each response
    #   Attributes: 
    #   - gen_ai. response.finish_reason = "stop" etc
    #   - gen_ai. usage.input_tokens = token count
    #   - gen_ai. usage.output_tokens = token count
    #
    # These spans are CHILDREN of the FastAPI span,
    # creating a proper nested trace tree: 
    #
    #   POST /v1/responses (FastAPI span)
    #     └─ agent. execute (worker span, if we add it)
    #       └─ invoke_agent {name} (Pydantic AI span, GenAI semantics)
    #         ├─ llm.request (Pydantic AI span)
    #         └─ llm.response (Pydantic AI span)
    #
    # All AUTOMATICALLY NESTED - no developer configuration needed!
    # All visible in Aspire Dashboard with token counts, latencies, etc.
    #
    # This works because:
    # 1. OpenTelemetry uses implicit context propagation (thread-local)
    # 2. Pydantic AI checks for active tracer at invocation time
    # 3. If one exists, it creates spans as children
    # 4. If none exists, it's a no-op
    #
    logger.info(
        f"OpenTelemetry initialized successfully.  "
        f"Tracing to {otlp_endpoint}. "
        f"Service: {service_name}"
    )
    logger.info(
        "Pydantic AI and other child libraries will auto-instrument with "
        "GenAI semantic conventions.  Spans will be properly nested in traces."
    )


def get_tracer(name: str = __name__):
    """
    Get a tracer for custom span creation.
    
    Use this if you want to create custom spans in your agent code.
    Usually not needed - Pydantic AI and FastAPI handle most spans. 
    
    Args:
        name: Module/package name for the tracer (usually __name__)
    
    Returns:
        OpenTelemetry Tracer instance
    
    Example:
        from python_agent_worker. telemetry import get_tracer
        
        tracer = get_tracer(__name__)
        
        # Create custom span around your agent logic
        with tracer.start_as_current_span("my_operation") as span:
            span.set_attribute("custom.field", "value")
            span.set_attribute("agent.name", "my-agent")
            # ... do work (child spans created here will be nested)
    """
    return trace.get_tracer(name)


def get_meter(name: str = __name__):
    """
    Get a meter for custom metric creation.
    
    Use this to record custom metrics (counters, histograms, etc.).
    
    Args:
        name: Module/package name for the meter (usually __name__)
    
    Returns:
        OpenTelemetry Meter instance
    
    Example:
        from python_agent_worker.telemetry import get_meter
        
        meter = get_meter(__name__)
        
        # Create custom counter
        request_counter = meter.create_counter(
            "custom.requests. total",
            description="Custom request counter"
        )
        request_counter.add(1, {"agent":  "my-agent"})
    """
    return metrics.get_meter(name)
```

---

### 2.5 Protocol Models

**File:** `agent_worker/protocol/models.py`

(Copied from existing PythonAgent sample with Pydantic v2 compatibility)

Key models: 
- `CreateResponse` - Gateway request format
- `StreamingResponseEvent` - Base event class
- `ResponseCreatedEvent`, `ResponseInProgressEvent`, `OutputItemAddedEvent`
- `OutputTextDeltaEvent`, `OutputTextDoneEvent`
- `ResponseCompletedEvent`, `ResponseFailedEvent`

All events match OpenAI Responses API format.

---

## 3. Package Structure & Exports

**File:** `agent_worker/__init__.py`

```python
"""
Python Agent Worker Framework

Lightweight, implementation-agnostic framework for building Python agents
compatible with AgentGateway. 

Public API:
    - WorkerAgent: Abstract base class for agents
    - Worker: FastAPI orchestrator for multiple agents
    - EventStreamContext: Context manager for event streaming
    - extract_input:  Utility for parsing gateway requests
    - setup_telemetry: One-line OpenTelemetry initialization
    - get_tracer: For custom spans
    - get_meter: For custom metrics
    
    + All StreamingResponseEvent types for type hints

Example: 
    from python_agent_worker import Worker, WorkerAgent, EventStreamContext, setup_telemetry
    
    class MyAgent(WorkerAgent):
        name = "my-agent"
        description = "Does something"
        
        async def execute(self, request, response_id):
            input_text = extract_input(request)
            async with EventStreamContext(response_id) as stream:
                result = await process(input_text)
                await stream.emit_text(result)
                async for event in stream.stream():
                    yield event
    
    worker = Worker([MyAgent()])
    setup_telemetry(worker.app)
    worker.run(port=5100)
"""

__version__ = "0.1.0"

from .agent import WorkerAgent
from .worker import Worker
from .helpers import EventStreamContext, extract_input
from .telemetry import setup_telemetry, get_tracer, get_meter

from . protocol.models import (
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
)

__all__ = [
    "WorkerAgent",
    "Worker",
    "EventStreamContext",
    "extract_input",
    "setup_telemetry",
    "get_tracer",
    "get_meter",
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
]
```

---

## 4. Configuration & Environment

### 4.1 pyproject.toml (Framework Package)

```toml
[project]
name = "python-agent-worker"
version = "0.1.0"
description = "Lightweight framework for Python agents compatible with AgentGateway"
readme = "README.md"
license = {text = "MIT"}
authors = [{name = "Microsoft Agent Framework"}]
requires-python = ">=3.10"

dependencies = [
    "fastapi>=0.104.0",
    "uvicorn[standard]>=0.24.0",
    "pydantic>=2.4.0",
    "sse-starlette>=2.1.0",
    # OpenTelemetry (mandatory)
    "opentelemetry-api>=1.20.0",
    "opentelemetry-sdk>=1.20.0",
    "opentelemetry-exporter-otlp>=0.41b0",
    "opentelemetry-instrumentation-fastapi>=0.41b0",
    "opentelemetry-instrumentation-logging>=0.41b0",
]

[project.optional-dependencies]
dev = [
    "pytest>=7.4.0",
    "pytest-asyncio>=0.21.0",
    "httpx>=0.25.0",
    "pytest-cov>=4.1.0",
]
examples = [
    "pydantic-ai>=0.7.0",
    "openai>=1.0.0",
]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"
```

### 4.2 Environment Variables

Standard OpenTelemetry environment variables: 

| Variable | Default | Purpose |
|----------|---------|---------|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` | OTLP collector endpoint |
| `OTEL_SERVICE_NAME` | `agent-worker` | Service name in telemetry |
| `OTEL_TRACES_EXPORTER` | `otlp` | Tracer exporter (or `none` to disable) |
| `OTEL_METRICS_EXPORTER` | `otlp` | Metrics exporter (or `none` to disable) |
| `OTEL_LOGS_EXPORTER` | `otlp` | Logs exporter (or `none` to disable) |

Example setup for Aspire: 

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=python-agent-worker
```

---

## 5. Testing Strategy

### 5.1 Unit Tests

**File:** `tests/test_agent.py`
- Test `WorkerAgent` ABC (cannot instantiate)
- Test subclass with valid implementation
- Test name/description validation
- Test custom span creation

**File:** `tests/test_worker.py`
- Test Worker initialization
- Test agent registration
- Test endpoint routing
- Test 404 for unknown agents
- Test SSE streaming format
- Test error responses

**File:** `tests/test_helpers.py`
- Test `EventStreamContext` lifecycle
- Test event sequence numbering
- Test text accumulation
- Test structured data emission
- Test exception handling in `__aexit__`
- Test `extract_input()` with various formats

**File:** `tests/test_telemetry.py`
- Test `setup_telemetry()` initialization
- Test span creation
- Test trace context propagation
- Test Pydantic AI span nesting (mock)
- Test console exporter activation

### 5.2 Integration Tests

**File:** `tests/integration/test_end_to_end.py`
- Create simple test agent
- Create Worker with test agent
- Send mock requests to endpoints
- Verify SSE event streaming
- Verify telemetry spans in mock exporter
- Verify Pydantic AI GenAI span attributes

### 5.3 Example Tests

All examples in `examples/` should have automated tests:
- `test_simple_agent.py`
- `test_pydantic_ai_agent.py`
- `test_agent_framework_agent.py`

---

## 6. Error Handling & Logging

### 6.1 Error Responses

| Status | Scenario | Response |
|--------|----------|----------|
| 400 | Invalid request format | `{"error": {"message": "Invalid request:  .. .", "type": "invalid_request_error"}}` |
| 404 | Agent not found | `{"error": {"message": "Agent 'X' not found", ... }, "available_agents": [... ]}` |
| 500 | Execution error | `{"error": {"message": "Agent execution failed:  ...", "type": "internal_error"}}` |

### 6.2 Logging Levels

| Level | Usage |
|-------|-------|
| DEBUG | Health checks, discovery calls, event emissions, span creations |
| INFO | Worker initialization, agent execution started, telemetry setup |
| WARNING | `extract_input()` parse failures, missing agent name |
| ERROR | Invalid requests, agent not found, execution failures |

All logs include trace context (trace_id, span_id) when OTEL is active.

---

## 7. Performance Characteristics

### 7.1 Expected Overhead

- Per-request:  <5ms for FastAPI routing/instrumentation
- Per-event: <1ms for JSON serialization
- Per-text-emission: <0.5ms for EventStreamContext
- Telemetry overhead: ~2-5ms per request (depends on OTLP exporter batch size)

### 7.2 Memory Usage

- Worker: ~1MB base + agent instances
- EventStreamContext: ~100KB + accumulated text size
- Telemetry: ~5-10MB (buffers, resource info)

---

## 8. Deployment

### 8.1 Docker Example

```dockerfile
FROM python:3.11-slim

WORKDIR /app
COPY pyproject.toml . 
RUN pip install -e . 

COPY src/ src/

ENV OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
ENV OTEL_SERVICE_NAME=python-agent-worker
ENV AGENT_WORKER_PORT=5100

EXPOSE 5100

CMD ["python", "-m", "src.agents.main"]
```

### 8.2 Kubernetes Example

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: python-agent-worker
spec: 
  containers:
  - name:  agent
    image: my-agent-worker:latest
    ports:
    - containerPort: 5100
    env:
    - name:  OTEL_EXPORTER_OTLP_ENDPOINT
      value: http://otel-collector:4317
    - name: OTEL_SERVICE_NAME
      value:  python-agent-worker
    livenessProbe:
      httpGet:
        path: /health
        port: 5100
      initialDelaySeconds: 10
      periodSeconds: 30
```

---

## 9. Version Compatibility

### 9.1 Python Versions

- Minimum: Python 3.10 (for modern type syntax)
- Recommended: Python 3.11+
- Tested: 3.10, 3.11, 3.12

### 9.2 Key Dependencies

| Package | Min Version | Purpose |
|---------|-------------|---------|
| fastapi | 0.104.0 | Web framework |
| uvicorn | 0.24.0 | ASGI server |
| pydantic | 2.4.0 | Data validation |
| sse-starlette | 2.1.0 | SSE streaming |
| opentelemetry-api | 1.20.0 | Tracing API |
| opentelemetry-sdk | 1.20.0 | Tracing SDK |

---

## 10. Security Considerations

### 10.1 Input Validation

- Agent names:  Alphanumeric + underscore only, max 128 chars
- Requests:  Pydantic validation ensures structure
- No file uploads supported (future consideration)

### 10.2 Error Messages

- Don't leak stack traces in HTTP responses (logged internally)
- Return generic error messages to clients
- Full exceptions logged at ERROR level with trace context

### 10.3 Assumed Security Model

- Authentication/authorization handled by gateway or infrastructure
- Worker assumes incoming requests are pre-validated
- No CORS, rate-limiting, or throttling (gateway's responsibility)

---

## 11. Future Extensibility

### 11.1 Middleware System (Phase 2)

```python
worker. add_middleware(
    RequestValidationMiddleware(),
    LoggingMiddleware(),
)
```

### 11.2 Agent Composition (Phase 2)

```python
combined_agent = CompositeAgent([
    agent1,
    agent2,
])
```

### 11.3 Custom Metrics (Phase 3)

```python
meter = get_meter(__name__)
token_counter = meter.create_counter("agent.tokens. used")
```

---

**Document Version:** 1.0  
**Last Updated:** 2025-12-24  
**Next Review:** After Phase 1 implementation