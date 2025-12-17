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


class ResponseUsage(BaseModel):
    """Usage statistics matching OpenAI Responses API."""
    input_tokens: int = 0
    output_tokens: int = 0
    total_tokens: int = 0
    input_tokens_details: dict[str, Any] | None = None
    output_tokens_details: dict[str, Any] | None = None


class ResponseStatus(BaseModel):
    """Response status object matching OpenAI Responses API."""
    id: str
    status: Literal["in_progress", "completed", "failed"] = "in_progress"
    object: str = "response"
    created_at: int
    output: list[Any] = Field(default_factory=list)
    usage: ResponseUsage = Field(default_factory=ResponseUsage)
    tools: list[Any] = Field(default_factory=list)


class ItemContent(BaseModel):
    """Content within an output item."""
    type: str
    text: str | None = None


class ItemResource(BaseModel):
    """Output item resource matching OpenAI Responses API."""
    id: str
    type: str = "message"
    object: str = "response.item"
    role: str = "assistant"
    content: list[ItemContent] = Field(default_factory=list)


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
    response: ResponseStatus


class ResponseInProgressEvent(StreamingResponseEvent):
    """Emitted when response generation is in progress."""
    type: Literal["response.in_progress"] = "response.in_progress"
    response: ResponseStatus


class OutputItemAddedEvent(StreamingResponseEvent):
    """Emitted when a new output item is added."""
    type: Literal["response.output_item.added"] = "response.output_item.added"
    item: ItemResource
    output_index: int = 0


class OutputTextDeltaEvent(StreamingResponseEvent):
    """Emitted for each chunk of generated text."""
    type: Literal["response.output_text.delta"] = "response.output_text.delta"
    item_id: str
    delta: str
    output_index: int = 0
    content_index: int = 0


class OutputTextDoneEvent(StreamingResponseEvent):
    """Emitted when text generation is complete."""
    type: Literal["response.output_text.done"] = "response.output_text.done"
    item_id: str
    text: str
    output_index: int = 0
    content_index: int = 0


class ResponseCompletedEvent(StreamingResponseEvent):
    """Emitted when response generation is complete."""
    type: Literal["response.completed"] = "response.completed"
    response: ResponseStatus


class ResponseFailedEvent(StreamingResponseEvent):
    """Emitted when response generation fails."""
    type: Literal["response.failed"] = "response.failed"
    response: ResponseStatus
    error: dict[str, Any]


class AgentCard(BaseModel):
    """
    Agent discovery card returned by GET /agents endpoint.
    """
    name: str
    description: str | None = None
