# Copyright (c) Microsoft. All rights reserved.

import os
import time
from typing import AsyncIterator
from pydantic import BaseModel
from pydantic_ai import Agent
from pydantic_ai.models.openai import OpenAIChatModel
from pydantic_ai.providers.azure import AzureProvider
from openai import AsyncAzureOpenAI
from azure.identity import DefaultAzureCredential, get_bearer_token_provider
import tiktoken

from ..models import (
    CreateResponse,
    ResponseCreatedEvent,
    ResponseInProgressEvent,
    OutputItemAddedEvent,
    OutputTextDeltaEvent,
    OutputTextDoneEvent,
    ResponseCompletedEvent,
    StreamingResponseEvent,
    ResponseStatus,
    ItemResource,
    ResponseUsage,
)


class Attraction(BaseModel):
    """A travel attraction."""
    name: str
    description: str
    address: str | None = None
    rating: float | None = None


class TravelItinerary(BaseModel):
    """Travel itinerary for a location."""
    location: str
    attractions: list[Attraction]
    additional_tips: str | None = None


# Initialize Azure OpenAI client and Pydantic AI agent
def get_travel_agent() -> Agent[None, TravelItinerary]:
    """
    Create and return a Pydantic AI agent configured with Azure OpenAI.
    
    The agent uses environment variables injected by Aspire:
    - AZURE_OPENAI_ENDPOINT: Azure OpenAI endpoint URL
    - MODEL_NAME: Azure OpenAI deployment/model name
    """
    # Get Azure OpenAI configuration from environment variables set by Aspire
    endpoint = os.environ.get("AZURE_OPENAI_ENDPOINT")
    model_name = os.environ.get("MODEL_NAME")
    
    if not endpoint:
        raise ValueError(
            "Azure OpenAI endpoint not found. "
            "Expected environment variable: AZURE_OPENAI_ENDPOINT"
        )
    
    if not model_name:
        raise ValueError(
            "Azure OpenAI model name not found. "
            "Expected environment variable: MODEL_NAME"
        )
    
    # Create Azure credential for authentication
    credential = DefaultAzureCredential()
    token_provider = get_bearer_token_provider(
        credential, 
        "https://cognitiveservices.azure.com/.default"
    )
    
    # Create Azure OpenAI client with token-based authentication
    client = AsyncAzureOpenAI(
        azure_endpoint=endpoint,
        azure_ad_token_provider=token_provider,
        api_version="2024-10-21",
    )
    
    # Create Pydantic AI model using AzureProvider with custom client
    model = OpenAIChatModel(
        model_name,
        provider=AzureProvider(openai_client=client),
    )
    
    # Create and configure the agent
    agent = Agent(
        model,
        output_type=TravelItinerary,
        system_prompt=(
            "You are an expert travel guide. "
            "Provide detailed information about popular attractions in the specified location. "
            "Include at least 3-5 attractions with descriptions, addresses when available, "
            "and ratings if known. Also provide helpful travel tips for the location."
        ),
    )
    
    return agent


async def execute_travel_itinerary_agent(
    request: CreateResponse,
    response_id: str,
) -> AsyncIterator[StreamingResponseEvent]:
    """
    Execute the travel itinerary agent using Pydantic AI and Azure OpenAI.
    
    Yields streaming events as the itinerary is generated.
    """
    sequence = 0
    
    # Extract input text
    if isinstance(request.input, str):
        input_text = request.input
    elif isinstance(request.input, list) and len(request.input) > 0:
        # Handle array of messages - take last user message
        for item in reversed(request.input):
            if isinstance(item, dict) and item.get("role") == "user":
                content = item.get("content", "")
                if isinstance(content, list) and len(content) > 0:
                    # Extract text from content array
                    for c in content:
                        if isinstance(c, dict) and c.get("type") == "input_text":
                            input_text = c.get("text", "")
                            break
                    else:
                        input_text = ""
                elif isinstance(content, str):
                    input_text = content
                else:
                    input_text = ""
                break
        else:
            input_text = ""
    else:
        input_text = ""
    
    # Emit response.created
    created_at = int(time.time())
    yield ResponseCreatedEvent(
        response=ResponseStatus(
            id=response_id,
            status="in_progress",
            created_at=created_at,
        ),
        sequence_number=sequence,
    )
    sequence += 1
    
    # Emit response.in_progress
    yield ResponseInProgressEvent(
        response=ResponseStatus(
            id=response_id,
            status="in_progress",
            created_at=created_at,
        ),
        sequence_number=sequence,
    )
    sequence += 1
    
    # Emit output_item.added
    item_id = f"{response_id}_item_0"
    yield OutputItemAddedEvent(
        item=ItemResource(
            id=item_id,
            type="message",
            role="assistant",
            content=[],
        ),
        output_index=0,
        sequence_number=sequence,
    )
    sequence += 1
    
    try:
        # Get the agent
        agent = get_travel_agent()
        
        # Run the agent to get the itinerary
        result = await agent.run(input_text)
        
        # Extract the structured output
        itinerary: TravelItinerary = result.output
        
        # Format the itinerary as text for streaming
        formatted_text = f"# Travel Itinerary for {itinerary.location}\n\n"
        formatted_text += "## Attractions:\n\n"
        
        for i, attraction in enumerate(itinerary.attractions, 1):
            formatted_text += f"### {i}. {attraction.name}\n"
            formatted_text += f"{attraction.description}\n"
            if attraction.address:
                formatted_text += f"**Address:** {attraction.address}\n"
            if attraction.rating:
                formatted_text += f"**Rating:** {attraction.rating}/5.0\n"
            formatted_text += "\n"
        
        if itinerary.additional_tips:
            formatted_text += f"## Travel Tips:\n{itinerary.additional_tips}\n"
        
        # Stream the formatted text in chunks (word-boundary aware)
        chunk_size = 100  # characters per chunk target
        full_text = []
        words = formatted_text.split()
        current_chunk = []
        current_length = 0
        
        for word in words:
            word_with_space = word + " "
            if current_length + len(word_with_space) > chunk_size and current_chunk:
                # Emit current chunk
                chunk = "".join(current_chunk)
                full_text.append(chunk)
                
                yield OutputTextDeltaEvent(
                    item_id=item_id,
                    delta=chunk,
                    output_index=0,
                    content_index=0,
                    sequence_number=sequence,
                )
                sequence += 1
                
                # Start new chunk
                current_chunk = [word_with_space]
                current_length = len(word_with_space)
            else:
                current_chunk.append(word_with_space)
                current_length += len(word_with_space)
        
        # Emit remaining chunk if any
        if current_chunk:
            chunk = "".join(current_chunk)
            full_text.append(chunk)
            
            yield OutputTextDeltaEvent(
                item_id=item_id,
                delta=chunk,
                output_index=0,
                content_index=0,
                sequence_number=sequence,
            )
            sequence += 1
        
        # Emit output_text.done
        final_text = "".join(full_text).rstrip()  # Remove trailing space
        yield OutputTextDoneEvent(
            item_id=item_id,
            text=final_text,
            output_index=0,
            content_index=0,
            sequence_number=sequence,
        )
        sequence += 1
        
        # Calculate usage using tiktoken for accurate token counting
        try:
            # Use cl100k_base encoding (used by gpt-4 and gpt-3.5-turbo)
            encoding = tiktoken.get_encoding("cl100k_base")
            input_token_count = len(encoding.encode(input_text))
            output_token_count = len(encoding.encode(final_text))
        except Exception:
            # Fallback to word count if tiktoken fails
            input_token_count = len(input_text.split())
            output_token_count = len(final_text.split())
        
        # Emit response.completed
        yield ResponseCompletedEvent(
            response=ResponseStatus(
                id=response_id,
                status="completed",
                created_at=created_at,
                usage=ResponseUsage(
                    input_tokens=input_token_count,
                    output_tokens=output_token_count,
                    total_tokens=input_token_count + output_token_count,
                ),
                tools=[],
            ),
            sequence_number=sequence,
        )
        
    except Exception as e:
        # On error, send a simple error message
        error_message = f"Error generating travel itinerary: {str(e)}"
        
        # Emit error as text
        yield OutputTextDeltaEvent(
            item_id=item_id,
            delta=error_message,
            output_index=0,
            content_index=0,
            sequence_number=sequence,
        )
        sequence += 1
        
        yield OutputTextDoneEvent(
            item_id=item_id,
            text=error_message,
            output_index=0,
            content_index=0,
            sequence_number=sequence,
        )
        sequence += 1
        
        # Emit response.completed (even on error)
        yield ResponseCompletedEvent(
            response=ResponseStatus(
                id=response_id,
                status="completed",
                created_at=created_at,
                usage=ResponseUsage(
                    input_tokens=0,
                    output_tokens=0,
                    total_tokens=0,
                ),
                tools=[],
            ),
            sequence_number=sequence,
        )
