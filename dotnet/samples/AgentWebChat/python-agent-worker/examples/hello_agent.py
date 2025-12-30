# Copyright (c) Microsoft. All rights reserved.

"""
Simple Hello World agent demonstrating the python-agent-worker framework.

This example shows the minimal code needed to create a functioning agent.
"""

from agent_worker import Worker, WorkerAgent, EventStreamContext


class HelloAgent(WorkerAgent):
    """Simple agent that greets the user."""
    
    def __init__(self):
        super().__init__(
            name="hello-agent",
            description="Greets the user with a friendly message"
        )
    
    async def execute(self, request, context):
        """Execute the greeting."""
        # Extract input text
        input_text = request.input if isinstance(request.input, str) else "World"
        
        # Use context manager to handle event streaming
        async with context:
            await context.emit_text(f"Hello, {input_text}! 👋")
            context.add_usage(input_tokens=len(input_text.split()), output_tokens=5)


# Create worker and register agent
worker = Worker(service_name="hello-worker")
worker.register_agent(HelloAgent())

# Export FastAPI app for uvicorn
app = worker.app

# To run: uvicorn examples.hello_agent:app --port 5100
