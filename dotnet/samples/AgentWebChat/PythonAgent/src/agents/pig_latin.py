# Copyright (c) Microsoft. All rights reserved.

"""
Pig Latin translation agent - refactored to use python-agent-worker framework.

Translates English text to Pig Latin.
"""

import re
from agent_worker import WorkerAgent, EventStreamContext


def translate_to_pig_latin(text: str) -> str:
    """
    Translate English text to Pig Latin.
    
    Rules:
    - Words starting with vowels: add "way" to the end
    - Words starting with consonants: move consonants to end and add "ay"
    - Preserve punctuation and capitalization patterns
    """
    def translate_word(word: str) -> str:
        # Extract leading/trailing punctuation
        leading = ""
        trailing = ""
        
        # Strip leading punctuation
        match = re.match(r'^([^a-zA-Z]*)', word)
        if match:
            leading = match.group(1)
            word = word[len(leading):]
        
        # Strip trailing punctuation
        match = re.search(r'([^a-zA-Z]*)$', word)
        if match:
            trailing = match.group(1)
            word = word[:len(word) - len(trailing)]
        
        if not word:
            return leading + trailing
        
        # Check if word was capitalized
        is_capitalized = word[0].isupper()
        word_lower = word.lower()
        
        # Translate based on first letter
        vowels = "aeiou"
        if word_lower[0] in vowels:
            # Starts with vowel: add "way"
            pig_latin = word_lower + "way"
        else:
            # Starts with consonant(s): move to end and add "ay"
            # Find first vowel
            first_vowel_idx = next(
                (i for i, char in enumerate(word_lower) if char in vowels),
                len(word_lower)
            )
            
            if first_vowel_idx == len(word_lower):
                # No vowels (edge case)
                pig_latin = word_lower + "ay"
            else:
                consonants = word_lower[:first_vowel_idx]
                rest = word_lower[first_vowel_idx:]
                pig_latin = rest + consonants + "ay"
        
        # Restore capitalization
        if is_capitalized:
            pig_latin = pig_latin.capitalize()
        
        return leading + pig_latin + trailing
    
    # Split into words and translate each
    words = text.split()
    translated_words = [translate_word(word) for word in words]
    return " ".join(translated_words)


class PigLatinAgent(WorkerAgent):
    """Agent that translates English text to Pig Latin."""
    
    def __init__(self):
        super().__init__(
            name="pig-latin-agent",
            description="Translates English text to Pig Latin"
        )
    
    async def execute(self, request, context: EventStreamContext):
        """Execute the pig latin translation."""
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
            # Translate to pig latin
            translated = translate_to_pig_latin(input_text)
            
            # Emit translated text (chunked for streaming effect)
            await context.emit_text(translated, chunk_size=50)
            
            # Add usage statistics
            input_token_count = len(input_text.split())
            output_token_count = len(translated.split())
            context.add_usage(
                input_tokens=input_token_count,
                output_tokens=output_token_count
            )
