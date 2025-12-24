# Copyright (c) Microsoft. All rights reserved.

"""
Main entry point for Python agent worker.

This module sets up the Worker and registers all available agents.
The Worker handles all infrastructure (FastAPI, telemetry, protocol).
"""

import logging
from agent_worker import Worker
from .agents.pig_latin import PigLatinAgent
from .agents.travel_itinerary import TravelItineraryAgent

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Create worker with telemetry enabled by default
worker = Worker(
    service_name="python-agent",
    title="Agent Worker",
    description="Python agent worker for AgentWebChat",
    version="0.1.0",
)

# Register agents
worker.register_agent(PigLatinAgent())
worker.register_agent(TravelItineraryAgent())

# Export FastAPI app for uvicorn
app = worker.app

logger.info(f"Worker initialized with {len(worker.agents)} agents")
