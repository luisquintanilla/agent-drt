# Copyright (c) Microsoft. All rights reserved.

"""
Python Agent Worker Framework

A lightweight framework for building Python agents that integrate seamlessly
with Microsoft AgentGateway infrastructure.

Features:
- Minimal boilerplate - focus on business logic, not protocol details
- Automatic telemetry with OpenTelemetry and GenAI semantic conventions
- Built-in event streaming and error handling
- FastAPI-based with automatic endpoint setup

Quick Start:
    >>> from agent_worker import Worker, WorkerAgent, EventStreamContext
    >>> 
    >>> class MyAgent(WorkerAgent):
    ...     def __init__(self):
    ...         super().__init__("my-agent", "Does something useful")
    ...     
    ...     async def execute(self, request, context):
    ...         async with context:
    ...             input_text = request.input if isinstance(request.input, str) else ""
    ...             result = f"Processed: {input_text}"
    ...             await context.emit_text(result)
    ...             context.add_usage(input_tokens=5, output_tokens=10)
    >>> 
    >>> # Set up worker
    >>> worker = Worker(service_name="my-worker")
    >>> worker.register_agent(MyAgent())
    >>> app = worker.app
    >>> 
    >>> # Run with: uvicorn module:app --port 5100

For more information, see README.md and documentation in docs/
"""

__version__ = "0.1.0"

from .agent import WorkerAgent
from .worker import Worker
from .helpers import EventStreamContext, stream_events
from .telemetry import setup_telemetry
from .protocol import (
    AgentResource,
    CreateResponse,
    ResponseUsage,
    ResponseStatus,
    ItemContent,
    ItemResource,
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
    "__version__",
    # Core classes
    "WorkerAgent",
    "Worker",
    "EventStreamContext",
    # Helpers
    "stream_events",
    "setup_telemetry",
    # Protocol models
    "AgentResource",
    "CreateResponse",
    "ResponseUsage",
    "ResponseStatus",
    "ItemContent",
    "ItemResource",
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
