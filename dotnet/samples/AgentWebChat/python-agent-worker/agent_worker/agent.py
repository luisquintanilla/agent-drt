# Copyright (c) Microsoft. All rights reserved.

"""
WorkerAgent: Abstract base class for gateway-compatible agents.

This module provides the core abstraction that agent developers subclass
to create agents compatible with AgentGateway.
"""

from abc import ABC, abstractmethod
from typing import AsyncIterator
from .protocol.models import CreateResponse, StreamingResponseEvent


class WorkerAgent(ABC):
    """
    Abstract base class for agents compatible with AgentGateway.
    
    Agent developers should subclass this and implement the execute() method.
    The framework handles all protocol, streaming, and telemetry concerns.
    
    Attributes:
        name: Unique agent identifier (alphanumeric + hyphens/underscores, max 128 chars).
              This is used for discovery and routing by the gateway.
        description: Human-readable description of what the agent does.
                    Shown in discovery responses and UI.
    
    Example:
        >>> class MyAgent(WorkerAgent):
        ...     def __init__(self):
        ...         super().__init__(
        ...             name="my-agent",
        ...             description="Does something useful"
        ...         )
        ...     
        ...     async def execute(self, request, context):
        ...         async with context:
        ...             await context.emit_text("Hello from my agent!")
    """
    
    def __init__(self, name: str, description: str):
        """
        Initialize the agent with name and description.
        
        Args:
            name: Unique agent identifier for discovery and routing
            description: Human-readable description of agent capabilities
        """
        self.name = name
        self.description = description
    
    @abstractmethod
    async def execute(
        self,
        request: CreateResponse,
        context: "EventStreamContext"
    ) -> None:
        """
        Execute the agent's business logic.
        
        This is the only method agent developers need to implement. The context
        manager provides helpers for emitting events without dealing with protocol details.
        
        Args:
            request: The parsed request from the gateway containing input, instructions,
                    metadata, etc.
            context: Event stream context manager that handles event creation,
                    sequencing, and streaming. Use context.emit_text() and other
                    methods to generate output.
        
        Example:
            >>> async def execute(self, request, context):
            ...     # Extract input
            ...     input_text = request.input if isinstance(request.input, str) else ""
            ...     
            ...     # Process with context manager
            ...     async with context:
            ...         result = await do_something(input_text)
            ...         await context.emit_text(result)
        
        Raises:
            Any exceptions raised will be caught by the framework and converted
            to ResponseFailedEvent automatically.
        """
        pass
