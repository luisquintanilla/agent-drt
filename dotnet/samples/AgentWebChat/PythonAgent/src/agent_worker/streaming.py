# Copyright (c) Microsoft. All rights reserved.

import json
from typing import AsyncIterator
from .models import StreamingResponseEvent


async def stream_events(
    events: AsyncIterator[StreamingResponseEvent],
) -> AsyncIterator[str]:
    """
    Convert Pydantic event objects to SSE format expected by gateway.
    
    Format: "data: {json}\\n\\n"
    """
    async for event in events:
        # Serialize event to JSON
        event_json = event.model_dump_json(exclude_none=True)
        
        # Format as SSE
        yield f"data: {event_json}\n\n"
