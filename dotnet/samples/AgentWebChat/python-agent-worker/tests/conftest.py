# Copyright (c) Microsoft. All rights reserved.

"""
Pytest configuration and fixtures.
"""

import pytest


@pytest.fixture
def anyio_backend():
    """Use asyncio backend for pytest-asyncio."""
    return "asyncio"
