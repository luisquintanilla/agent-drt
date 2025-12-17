# Copyright (c) Microsoft. All rights reserved.

import re
from typing import AsyncIterator
from ..models import (
    CreateResponse,
    ResponseCreatedEvent,
    ResponseInProgressEvent,
    OutputItemAddedEvent,
    OutputTextDeltaEvent,
    OutputTextDoneEvent,
    ResponseCompletedEvent,
    StreamingResponseEvent,
)


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


async def execute_pig_latin_agent(
    request: CreateResponse,
    response_id: str,
) -> AsyncIterator[StreamingResponseEvent]:
    """
    Execute the pig latin translation agent.
    
    Yields streaming events as the translation is performed.
    """
    from ..models import ResponseStatus, ItemResource, ItemContent
    import time
    
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
            content=[],
        ),
        output_index=0,
        sequence_number=sequence,
    )
    sequence += 1
    
    # Translate to pig latin
    translated = translate_to_pig_latin(input_text)
    
    # Stream the translation in chunks (simulate streaming)
    chunk_size = 10  # words per chunk
    words = translated.split()
    full_text = []
    
    for i in range(0, len(words), chunk_size):
        chunk_words = words[i:i + chunk_size]
        chunk_text = " ".join(chunk_words)
        
        # Add space if not first chunk
        if i > 0:
            chunk_text = " " + chunk_text
        
        full_text.append(chunk_text)
        
        # Emit delta
        yield OutputTextDeltaEvent(
            item_id=item_id,
            delta=chunk_text,
            output_index=0,
            content_index=0,
            sequence_number=sequence,
        )
        sequence += 1
    
    # Emit output_text.done
    yield OutputTextDoneEvent(
        item_id=item_id,
        text="".join(full_text),
        output_index=0,
        content_index=0,
        sequence_number=sequence,
    )
    sequence += 1
    
    # Emit response.completed
    yield ResponseCompletedEvent(
        response=ResponseStatus(
            id=response_id,
            status="completed",
        ),
        sequence_number=sequence,
    )
