# Copyright (c) Microsoft. All rights reserved.

"""
Worker: FastAPI orchestrator for AgentGateway-compatible workers.

This module provides the main Worker class that handles all infrastructure:
FastAPI app setup, agent registration, request routing, and telemetry.
"""

import logging
import uuid
from typing import Optional
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from sse_starlette.sse import EventSourceResponse

from .agent import WorkerAgent
from .protocol.models import CreateResponse, AgentCard
from .helpers import EventStreamContext, stream_events
from .telemetry import setup_telemetry

logger = logging.getLogger(__name__)


class Worker:
    """
    FastAPI orchestrator for AgentGateway workers.
    
    This class handles all the infrastructure concerns:
    - FastAPI application setup
    - Agent discovery and registration
    - Request routing to correct agent
    - Response streaming and error handling
    - Integrated telemetry (OpenTelemetry with GenAI semantic conventions)
    
    Agent developers just register their WorkerAgent subclasses and the
    framework handles the rest.
    
    Example:
        >>> from agent_worker import Worker, WorkerAgent, EventStreamContext
        >>> 
        >>> class MyAgent(WorkerAgent):
        ...     def __init__(self):
        ...         super().__init__("my-agent", "Does something cool")
        ...     
        ...     async def execute(self, request, context):
        ...         async with context:
        ...             await context.emit_text("Hello!")
        >>> 
        >>> # Create worker and register agent
        >>> worker = Worker(service_name="my-worker")
        >>> worker.register_agent(MyAgent())
        >>> 
        >>> # Get FastAPI app
        >>> app = worker.app
        >>> 
        >>> # Run with: uvicorn module:app
    """
    
    def __init__(
        self,
        service_name: str = "python-agent-worker",
        title: str = "Agent Worker",
        description: str = "Python agent worker for AgentWebChat",
        version: str = "0.1.0",
        enable_telemetry: bool = True,
        otlp_endpoint: Optional[str] = None,
    ):
        """
        Initialize the worker with FastAPI app and telemetry.
        
        Args:
            service_name: Service name for telemetry identification
            title: FastAPI app title
            description: FastAPI app description
            version: FastAPI app version
            enable_telemetry: Whether to enable OpenTelemetry (default: True)
            otlp_endpoint: Optional OTLP endpoint override
        """
        self.service_name = service_name
        self.agents: dict[str, WorkerAgent] = {}
        
        # Create FastAPI app
        self.app = FastAPI(
            title=title,
            description=description,
            version=version,
        )
        
        # Set up telemetry (required, not optional)
        if enable_telemetry:
            self.tracer = setup_telemetry(
                self.app,
                service_name=service_name,
                otlp_endpoint=otlp_endpoint,
            )
            logger.info(f"Telemetry enabled for service: {service_name}")
        else:
            self.tracer = None
            logger.warning("Telemetry disabled - not recommended for production")
        
        # Register endpoints
        self._setup_endpoints()
    
    def register_agent(self, agent: WorkerAgent):
        """
        Register an agent with this worker.
        
        Args:
            agent: WorkerAgent instance to register
        
        Raises:
            ValueError: If agent name is already registered
        
        Example:
            >>> worker = Worker()
            >>> worker.register_agent(MyAgent())
        """
        if agent.name in self.agents:
            raise ValueError(f"Agent '{agent.name}' is already registered")
        
        self.agents[agent.name] = agent
        logger.info(f"Registered agent: {agent.name}")
    
    def _setup_endpoints(self):
        """Set up FastAPI endpoints for AgentGateway protocol."""
        
        @self.app.get("/health")
        async def health_check():
            """
            Health check endpoint.
            
            Gateway's WorkerHealthCheckService probes this endpoint.
            """
            logger.info("Health check endpoint called")
            return {"status": "healthy"}
        
        @self.app.get("/v1/entities")
        async def list_agents():
            """
            Agent discovery endpoint.
            
            Gateway's WorkerDiscoveryCache queries this to find
            which agents this worker supports.
            
            Returns DiscoveryResponse format: {"entities": [...]}
            """
            logger.info(f"Agent discovery called - returning {len(self.agents)} agents")
            return {
                "entities": [
                    {
                        "name": agent.name,
                        "description": agent.description,
                    }
                    for agent in self.agents.values()
                ]
            }
        
        @self.app.post("/v1/responses")
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
            
            logger.info(f"Received request for agent: {agent_name}")
            
            if not agent_name or agent_name not in self.agents:
                logger.error(f"Agent '{agent_name}' not found")
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
            
            logger.info(f"Executing agent '{agent_name}' with response ID: {response_id}")
            
            # Get agent
            agent = self.agents[agent_name]
            
            # Execute agent with event stream context
            events: list = []
            context = EventStreamContext(response_id, events)
            
            try:
                await agent.execute(request, context)
            except Exception as e:
                logger.error(f"Error executing agent '{agent_name}': {e}", exc_info=True)
                # Error is already handled by EventStreamContext
            
            # Convert to SSE format
            async def event_generator():
                async for event_json in stream_events(events):
                    yield event_json
            
            return EventSourceResponse(event_generator())
        
        @self.app.get("/")
        async def root():
            """Root endpoint with service info."""
            logger.info("Root endpoint called")
            return {
                "service": self.service_name,
                "version": self.app.version,
                "agents": len(self.agents),
            }
