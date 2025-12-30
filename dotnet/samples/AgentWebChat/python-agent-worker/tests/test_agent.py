# Copyright (c) Microsoft. All rights reserved.

"""
Tests for WorkerAgent base class.
"""

import pytest
from agent_worker import WorkerAgent, EventStreamContext


class SimpleTestAgent(WorkerAgent):
    """Test agent implementation."""
    
    def __init__(self):
        super().__init__(
            name="test-agent",
            description="Test agent for unit tests"
        )
    
    async def execute(self, request, context: EventStreamContext):
        """Simple test execution."""
        async with context:
            await context.emit_text("Test output")
            context.add_usage(input_tokens=5, output_tokens=2)


def test_worker_agent_initialization():
    """Test WorkerAgent can be instantiated."""
    agent = SimpleTestAgent()
    assert agent.name == "test-agent"
    assert agent.description == "Test agent for unit tests"


@pytest.mark.asyncio
async def test_worker_agent_execute():
    """Test WorkerAgent execute method."""
    from agent_worker.protocol import CreateResponse
    
    agent = SimpleTestAgent()
    request = CreateResponse(input="test input")
    
    events = []
    context = EventStreamContext("test-response-id", events)
    
    await agent.execute(request, context)
    
    # Verify events were collected
    assert len(events) > 0
