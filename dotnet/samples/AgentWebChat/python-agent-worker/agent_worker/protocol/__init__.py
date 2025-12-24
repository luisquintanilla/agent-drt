# Copyright (c) Microsoft. All rights reserved.

"""
Protocol models for AgentGateway communication.

This module provides Pydantic models that match the AgentGateway's
OpenAI Responses API-compatible protocol.
"""

from .models import (
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
