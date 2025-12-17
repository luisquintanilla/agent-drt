# Copyright (c) Microsoft. All rights reserved.

import json
from typing import AsyncIterator
from .models import StreamingResponseEvent


async def stream_events(
    events: AsyncIterator[StreamingResponseEvent],
) -> AsyncIterator[str]:
    """
    Convert Pydantic event objects to JSON strings for EventSourceResponse.
    
    EventSourceResponse automatically adds "data: " prefix and "\n\n" delimiter.
    """
    async for event in events:
        # Serialize event to JSON (EventSourceResponse handles SSE formatting)
        yield event.model_dump_json(exclude_none=True)
