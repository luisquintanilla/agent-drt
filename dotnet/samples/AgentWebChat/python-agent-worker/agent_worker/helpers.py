# Copyright (c) Microsoft. All rights reserved.

"""
Event streaming helpers for AgentGateway protocol.

This module provides context managers and utilities to simplify
event creation, sequencing, and error handling.
"""

import time
from typing import AsyncIterator, Optional
from .protocol.models import (
    ResponseStatus,
    ResponseUsage,
    ItemResource,
    ItemContent,
    StreamingResponseEvent,
    ResponseCreatedEvent,
    ResponseInProgressEvent,
    OutputItemAddedEvent,
    OutputTextDeltaEvent,
    OutputTextDoneEvent,
    ResponseCompletedEvent,
    ResponseFailedEvent,
)


class EventStreamContext:
    """
    Context manager for streaming AgentGateway protocol events.
    
    This class handles all the boilerplate of event creation, sequencing,
    and protocol compliance. Agent developers just call simple methods
    like emit_text() without worrying about event types, sequence numbers,
    or response status.
    
    The context manager automatically emits:
    - response.created on enter
    - response.in_progress on enter
    - response.output_item.added on enter
    - response.completed or response.failed on exit
    
    Example:
        >>> async with EventStreamContext(response_id, events_queue) as context:
        ...     await context.emit_text("Hello")
        ...     await context.emit_text(" world")
        ...     context.add_usage(input_tokens=5, output_tokens=2)
    """
    
    def __init__(
        self,
        response_id: str,
        events: list[StreamingResponseEvent],
    ):
        """
        Initialize event stream context.
        
        Args:
            response_id: Unique response identifier
            events: List to append events to (will be streamed by framework)
        """
        self.response_id = response_id
        self.events = events
        self.sequence = 0
        self.created_at = int(time.time())
        self.item_id = f"{response_id}_item_0"
        self.accumulated_text: list[str] = []
        self.usage = ResponseUsage()
        self.error: Optional[dict] = None
        self._entered = False
    
    async def __aenter__(self):
        """
        Enter context - emit initial events.
        
        Automatically emits:
        - response.created
        - response.in_progress
        - response.output_item.added
        """
        self._entered = True
        
        # Emit response.created
        self.events.append(
            ResponseCreatedEvent(
                response=ResponseStatus(
                    id=self.response_id,
                    status="in_progress",
                    created_at=self.created_at,
                ),
                sequence_number=self.sequence,
            )
        )
        self.sequence += 1
        
        # Emit response.in_progress
        self.events.append(
            ResponseInProgressEvent(
                response=ResponseStatus(
                    id=self.response_id,
                    status="in_progress",
                    created_at=self.created_at,
                ),
                sequence_number=self.sequence,
            )
        )
        self.sequence += 1
        
        # Emit output_item.added
        self.events.append(
            OutputItemAddedEvent(
                item=ItemResource(
                    id=self.item_id,
                    type="message",
                    role="assistant",
                    content=[],
                ),
                output_index=0,
                sequence_number=self.sequence,
            )
        )
        self.sequence += 1
        
        return self
    
    async def __aexit__(self, exc_type, exc_val, exc_tb):
        """
        Exit context - emit final events.
        
        If no exception occurred:
        - Emits response.output_text.done with accumulated text
        - Emits response.completed with usage stats
        
        If an exception occurred:
        - Emits error message as text
        - Emits response.failed with error details
        
        Returns:
            False to propagate exceptions, True to suppress them
        """
        if exc_type is not None:
            # Error occurred - emit failure event
            error_message = f"Error: {str(exc_val)}"
            
            # Emit error as text delta
            self.events.append(
                OutputTextDeltaEvent(
                    item_id=self.item_id,
                    delta=error_message,
                    output_index=0,
                    content_index=0,
                    sequence_number=self.sequence,
                )
            )
            self.sequence += 1
            
            # Emit text done
            self.events.append(
                OutputTextDoneEvent(
                    item_id=self.item_id,
                    text=error_message,
                    output_index=0,
                    content_index=0,
                    sequence_number=self.sequence,
                )
            )
            self.sequence += 1
            
            # Emit response.failed
            self.events.append(
                ResponseFailedEvent(
                    response=ResponseStatus(
                        id=self.response_id,
                        status="failed",
                        created_at=self.created_at,
                        usage=self.usage,
                    ),
                    error={
                        "message": str(exc_val),
                        "type": exc_type.__name__ if exc_type else "error",
                    },
                    sequence_number=self.sequence,
                )
            )
            
            # Suppress the exception (already handled)
            return True
        
        # Success - emit completion events
        final_text = "".join(self.accumulated_text)
        
        # Emit output_text.done
        self.events.append(
            OutputTextDoneEvent(
                item_id=self.item_id,
                text=final_text,
                output_index=0,
                content_index=0,
                sequence_number=self.sequence,
            )
        )
        self.sequence += 1
        
        # Emit response.completed
        self.events.append(
            ResponseCompletedEvent(
                response=ResponseStatus(
                    id=self.response_id,
                    status="completed",
                    created_at=self.created_at,
                    usage=self.usage,
                    tools=[],
                ),
                sequence_number=self.sequence,
            )
        )
        
        return False
    
    async def emit_text(self, text: str, chunk_size: Optional[int] = None):
        """
        Emit text output, optionally chunked for streaming effect.
        
        Args:
            text: Text to emit
            chunk_size: Optional size to chunk text. If None, emits all at once.
                       Useful for simulating streaming from non-streaming sources.
        
        Example:
            >>> await context.emit_text("Hello world")  # Emit all at once
            >>> await context.emit_text(long_text, chunk_size=100)  # Chunked
        """
        if not self._entered:
            raise RuntimeError("emit_text called outside of context manager")
        
        if chunk_size is None:
            # Emit all at once
            self.accumulated_text.append(text)
            self.events.append(
                OutputTextDeltaEvent(
                    item_id=self.item_id,
                    delta=text,
                    output_index=0,
                    content_index=0,
                    sequence_number=self.sequence,
                )
            )
            self.sequence += 1
        else:
            # Emit in chunks
            for i in range(0, len(text), chunk_size):
                chunk = text[i:i + chunk_size]
                self.accumulated_text.append(chunk)
                self.events.append(
                    OutputTextDeltaEvent(
                        item_id=self.item_id,
                        delta=chunk,
                        output_index=0,
                        content_index=0,
                        sequence_number=self.sequence,
                    )
                )
                self.sequence += 1
    
    def add_usage(
        self,
        input_tokens: int = 0,
        output_tokens: int = 0,
        input_tokens_details: Optional[dict] = None,
        output_tokens_details: Optional[dict] = None,
    ):
        """
        Add token usage statistics.
        
        Args:
            input_tokens: Number of input tokens consumed
            output_tokens: Number of output tokens generated
            input_tokens_details: Optional detailed input token breakdown
            output_tokens_details: Optional detailed output token breakdown
        
        Example:
            >>> context.add_usage(input_tokens=10, output_tokens=25)
        """
        self.usage.input_tokens = input_tokens
        self.usage.output_tokens = output_tokens
        self.usage.total_tokens = input_tokens + output_tokens
        if input_tokens_details:
            self.usage.input_tokens_details = input_tokens_details
        if output_tokens_details:
            self.usage.output_tokens_details = output_tokens_details


async def stream_events(
    events: list[StreamingResponseEvent],
) -> AsyncIterator[str]:
    """
    Convert Pydantic event objects to JSON strings for EventSourceResponse.
    
    EventSourceResponse automatically adds "data: " prefix and "\n\n" delimiter.
    
    Args:
        events: List of events to stream
    
    Yields:
        JSON strings for each event
    """
    for event in events:
        yield event.model_dump_json(exclude_none=True)
