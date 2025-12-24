# Copyright (c) Microsoft. All rights reserved.

"""
Tests for Worker orchestrator.
"""

import pytest
from agent_worker import Worker, WorkerAgent, EventStreamContext


class MockAgent(WorkerAgent):
    """Mock agent for testing."""
    
    def __init__(self):
        super().__init__(
            name="mock-agent",
            description="Mock agent for testing"
        )
    
    async def execute(self, request, context: EventStreamContext):
        """Execute mock agent."""
        async with context:
            await context.emit_text("Mock response")


def test_worker_initialization():
    """Test Worker can be created."""
    worker = Worker(service_name="test-worker", enable_telemetry=False)
    assert worker.service_name == "test-worker"
    assert len(worker.agents) == 0
    assert worker.app is not None


def test_worker_register_agent():
    """Test agent registration."""
    worker = Worker(service_name="test-worker", enable_telemetry=False)
    agent = MockAgent()
    
    worker.register_agent(agent)
    
    assert len(worker.agents) == 1
    assert "mock-agent" in worker.agents
    assert worker.agents["mock-agent"] == agent


def test_worker_register_duplicate_agent():
    """Test registering duplicate agent raises error."""
    worker = Worker(service_name="test-worker", enable_telemetry=False)
    agent1 = MockAgent()
    agent2 = MockAgent()
    
    worker.register_agent(agent1)
    
    with pytest.raises(ValueError, match="already registered"):
        worker.register_agent(agent2)


@pytest.mark.asyncio
async def test_worker_health_endpoint():
    """Test health check endpoint."""
    from httpx import ASGITransport, AsyncClient
    
    worker = Worker(service_name="test-worker", enable_telemetry=False)
    
    async with AsyncClient(
        transport=ASGITransport(app=worker.app), 
        base_url="http://test"
    ) as client:
        response = await client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "healthy"}


@pytest.mark.asyncio
async def test_worker_entities_endpoint():
    """Test entity discovery endpoint."""
    from httpx import ASGITransport, AsyncClient
    
    worker = Worker(service_name="test-worker", enable_telemetry=False)
    worker.register_agent(MockAgent())
    
    async with AsyncClient(
        transport=ASGITransport(app=worker.app), 
        base_url="http://test"
    ) as client:
        response = await client.get("/v1/entities")
        assert response.status_code == 200
        data = response.json()
        assert "entities" in data
        assert len(data["entities"]) == 1
        assert data["entities"][0]["name"] == "mock-agent"
        assert data["entities"][0]["description"] == "Mock agent for testing"


@pytest.mark.asyncio
async def test_worker_root_endpoint():
    """Test root endpoint."""
    from httpx import ASGITransport, AsyncClient
    
    worker = Worker(service_name="test-worker", enable_telemetry=False)
    worker.register_agent(MockAgent())
    
    async with AsyncClient(
        transport=ASGITransport(app=worker.app), 
        base_url="http://test"
    ) as client:
        response = await client.get("/")
        assert response.status_code == 200
        data = response.json()
        assert data["service"] == "test-worker"
        assert data["agents"] == 1
