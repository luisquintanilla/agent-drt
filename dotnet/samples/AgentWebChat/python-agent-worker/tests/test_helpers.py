# Copyright (c) Microsoft. All rights reserved.

"""
Tests for EventStreamContext.
"""

import pytest
from agent_worker.helpers import EventStreamContext
from agent_worker.protocol import (
    ResponseCreatedEvent,
    ResponseInProgressEvent,
    OutputItemAddedEvent,
    OutputTextDeltaEvent,
    OutputTextDoneEvent,
    ResponseCompletedEvent,
)


@pytest.mark.asyncio
async def test_event_stream_context_basic_flow():
    """Test basic event streaming flow."""
    events = []
    context = EventStreamContext("test-response-id", events)
    
    async with context:
        await context.emit_text("Hello, world!")
        context.add_usage(input_tokens=5, output_tokens=3)
    
    # Verify event sequence
    assert len(events) >= 5  # created, in_progress, item_added, delta(s), done, completed
    
    # Check event types
    assert isinstance(events[0], ResponseCreatedEvent)
    assert isinstance(events[1], ResponseInProgressEvent)
    assert isinstance(events[2], OutputItemAddedEvent)
    assert any(isinstance(e, OutputTextDeltaEvent) for e in events)
    assert any(isinstance(e, OutputTextDoneEvent) for e in events)
    assert isinstance(events[-1], ResponseCompletedEvent)


@pytest.mark.asyncio
async def test_event_stream_context_chunking():
    """Test text chunking."""
    events = []
    context = EventStreamContext("test-response-id", events)
    
    async with context:
        # Emit text with chunking
        await context.emit_text("Hello, world! This is a test.", chunk_size=5)
    
    # Count delta events
    delta_events = [e for e in events if isinstance(e, OutputTextDeltaEvent)]
    assert len(delta_events) > 1  # Should be chunked


@pytest.mark.asyncio
async def test_event_stream_context_usage():
    """Test usage tracking."""
    events = []
    context = EventStreamContext("test-response-id", events)
    
    async with context:
        await context.emit_text("Test")
        context.add_usage(input_tokens=10, output_tokens=20)
    
    # Find completion event and check usage
    completed = [e for e in events if isinstance(e, ResponseCompletedEvent)][0]
    assert completed.response.usage.input_tokens == 10
    assert completed.response.usage.output_tokens == 20
    assert completed.response.usage.total_tokens == 30


@pytest.mark.asyncio
async def test_event_stream_context_error_handling():
    """Test error handling in context manager."""
    from agent_worker.protocol import ResponseFailedEvent
    
    events = []
    context = EventStreamContext("test-response-id", events)
    
    # Simulate an error
    async with context:
        await context.emit_text("Starting...")
        raise ValueError("Test error")
    
    # Should have a failed event
    failed_events = [e for e in events if isinstance(e, ResponseFailedEvent)]
    assert len(failed_events) == 1
    assert "Test error" in failed_events[0].error["message"]


@pytest.mark.asyncio
async def test_event_stream_context_sequence_numbers():
    """Test sequence numbers increment correctly."""
    events = []
    context = EventStreamContext("test-response-id", events)
    
    async with context:
        await context.emit_text("Test")
    
    # Verify sequence numbers are sequential
    for i, event in enumerate(events):
        assert event.sequence_number == i
