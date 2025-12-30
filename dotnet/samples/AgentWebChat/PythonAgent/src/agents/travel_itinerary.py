# Copyright (c) Microsoft. All rights reserved.

"""
Travel itinerary agent - refactored to use python-agent-worker framework.

Generates detailed travel itineraries using Pydantic AI and Azure OpenAI.
"""

import os
import tiktoken
from pydantic import BaseModel
from pydantic_ai import Agent as PydanticAgent
from pydantic_ai.models.openai import OpenAIChatModel
from pydantic_ai.providers.azure import AzureProvider
from openai import AsyncAzureOpenAI
from azure.identity import DefaultAzureCredential, get_bearer_token_provider

from agent_worker import WorkerAgent, EventStreamContext


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


class TravelItineraryAgent(WorkerAgent):
    """Agent that generates travel itineraries using Pydantic AI."""
    
    def __init__(self):
        super().__init__(
            name="travel-itinerary-agent",
            description="Generates detailed travel itineraries for locations with attractions and tips"
        )
        self.ai_agent = None  # Lazy initialization
    
    def _get_ai_agent(self) -> PydanticAgent:
        """Get or create the Pydantic AI agent."""
        if self.ai_agent is None:
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
            self.ai_agent = PydanticAgent(
                model,
                output_type=TravelItinerary,
                system_prompt=(
                    "You are an expert travel guide. "
                    "Provide detailed information about popular attractions in the specified location. "
                    "Include at least 3-5 attractions with descriptions, addresses when available, "
                    "and ratings if known. Also provide helpful travel tips for the location."
                ),
            )
        
        return self.ai_agent
    
    async def execute(self, request, context: EventStreamContext):
        """Execute the travel itinerary generation."""
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
        
        # Use context manager to handle event streaming
        async with context:
            # Get the AI agent
            agent = self._get_ai_agent()
            
            # Run the agent to get the itinerary
            # Pydantic AI is automatically instrumented with GenAI semantic conventions
            result = await agent.run(input_text)
            
            # Extract the structured output
            itinerary: TravelItinerary = result.output
            
            # Format the itinerary as text
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
            
            # Emit text (chunked for streaming effect)
            await context.emit_text(formatted_text, chunk_size=100)
            
            # Calculate usage using tiktoken
            try:
                encoding = tiktoken.get_encoding("cl100k_base")
                input_token_count = len(encoding.encode(input_text))
                output_token_count = len(encoding.encode(formatted_text))
            except Exception:
                # Fallback to word count if tiktoken fails
                input_token_count = len(input_text.split())
                output_token_count = len(formatted_text.split())
            
            context.add_usage(
                input_tokens=input_token_count,
                output_tokens=output_token_count
            )
