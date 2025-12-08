# Copyright (c) Microsoft. All rights reserved.

from typing import Any, Literal
from pydantic import BaseModel, Field


class AgentResource(BaseModel):
    """Agent identifier in the request."""
    name: str


class CreateResponse(BaseModel):
    """
    Request schema for POST /v1/responses endpoint.
    Subset of OpenAI Responses API specification.
    """
    agent: AgentResource | None = None
    model: str | None = None  # Fallback if agent not provided
    input: str | list[dict[str, Any]]
    instructions: str | None = None
    metadata: dict[str, str] | None = None
    stream: bool = False
    conversation_id: str | None = None


class StreamingResponseEvent(BaseModel):
    """
    Base event type for SSE streaming responses.
    Gateway expects events in this format.
    """
    type: str
    sequence_number: int = 0


class ResponseCreatedEvent(StreamingResponseEvent):
    """Emitted when response generation starts."""
    type: Literal["response.created"] = "response.created"
    response_id: str


class ResponseInProgressEvent(StreamingResponseEvent):
    """Emitted when response generation is in progress."""
    type: Literal["response.in_progress"] = "response.in_progress"


class OutputItemAddedEvent(StreamingResponseEvent):
    """Emitted when a new output item is added."""
    type: Literal["response.output_item.added"] = "response.output_item.added"
    item_id: str
    output_index: int = 0


class OutputTextDeltaEvent(StreamingResponseEvent):
    """Emitted for each chunk of generated text."""
    type: Literal["response.output_text.delta"] = "response.output_text.delta"
    delta: str
    output_index: int = 0


class OutputTextDoneEvent(StreamingResponseEvent):
    """Emitted when text generation is complete."""
    type: Literal["response.output_text.done"] = "response.output_text.done"
    text: str
    output_index: int = 0


class ResponseCompletedEvent(StreamingResponseEvent):
    """Emitted when response generation is complete."""
    type: Literal["response.completed"] = "response.completed"


class ResponseFailedEvent(StreamingResponseEvent):
    """Emitted when response generation fails."""
    type: Literal["response.failed"] = "response.failed"
    error: dict[str, Any]


class AgentCard(BaseModel):
    """
    Agent discovery card returned by GET /agents endpoint.
    """
    name: str
    description: str | None = None
