# Copyright (c) Microsoft. All rights reserved.

"""
OpenTelemetry telemetry setup with GenAI semantic conventions.

This module provides automatic telemetry configuration for Python agents,
including tracing, metrics, and logging with OTLP export to Aspire dashboard.
"""

import logging
import os
from typing import Optional
from opentelemetry import metrics, trace
# Note: The logs API in OpenTelemetry Python is still in development and not yet stable.
# The underscore-prefixed imports below are the current recommended way to use logs.
# See: https://opentelemetry.io/docs/languages/python/
from opentelemetry._logs import set_logger_provider
from opentelemetry.exporter.otlp.proto.grpc._log_exporter import OTLPLogExporter
from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk._logs import LoggerProvider, LoggingHandler
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.logging import LoggingInstrumentor


def setup_telemetry(
    app,
    service_name: str = "python-agent-worker",
    otlp_endpoint: Optional[str] = None,
) -> trace.Tracer:
    """
    Configure OpenTelemetry for FastAPI application.
    
    This function sets up tracing, metrics, and logging to export to the Aspire dashboard
    via OTLP (OpenTelemetry Protocol). It configures providers, exporters, and instruments
    FastAPI and logging automatically.
    
    Note: This function does NOT automatically instrument AI frameworks. Users should
    explicitly instrument their chosen AI framework (e.g., Pydantic AI, OpenAI SDK)
    after calling this function to ensure proper trace context.
    
    Args:
        app: The FastAPI application instance
        service_name: The name to identify this service in telemetry data
        otlp_endpoint: Optional OTLP endpoint URL. If not provided, reads from
                      OTEL_EXPORTER_OTLP_ENDPOINT env var or uses http://localhost:4317
        
    Returns:
        A tracer instance for creating custom spans
        
    Example:
        >>> from fastapi import FastAPI
        >>> from agent_worker.telemetry import setup_telemetry
        >>> from pydantic_ai import Agent, InstrumentationSettings
        >>> 
        >>> app = FastAPI()
        >>> tracer = setup_telemetry(app, service_name="my-agent")
        >>> 
        >>> # Explicitly instrument your AI framework
        >>> Agent.instrument_all(InstrumentationSettings(version=3, include_content=True))
    """
    # Get OTLP endpoint from parameter, environment, or use default
    if otlp_endpoint is None:
        otlp_endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")
    
    # Create resource with service name
    resource = Resource.create({
        "service.name": service_name,
    })
    
    # Configure Tracing
    trace_provider = TracerProvider(resource=resource)
    trace_provider.add_span_processor(
        BatchSpanProcessor(
            OTLPSpanExporter(endpoint=otlp_endpoint)
        )
    )
    trace.set_tracer_provider(trace_provider)
    
    # Configure Metrics
    metric_reader = PeriodicExportingMetricReader(
        OTLPMetricExporter(endpoint=otlp_endpoint)
    )
    meter_provider = MeterProvider(
        resource=resource,
        metric_readers=[metric_reader]
    )
    metrics.set_meter_provider(meter_provider)
    
    # Configure Logging
    logger_provider = LoggerProvider(resource=resource)
    logger_provider.add_log_record_processor(
        BatchLogRecordProcessor(
            OTLPLogExporter(endpoint=otlp_endpoint)
        )
    )
    set_logger_provider(logger_provider)
    
    # Instrument logging to include trace context
    LoggingInstrumentor().instrument(set_logging_format=True)
    
    # Add logging handler to root logger
    handler = LoggingHandler(
        level=logging.NOTSET,
        logger_provider=logger_provider
    )
    logging.getLogger().addHandler(handler)
    
    # Instrument FastAPI application
    FastAPIInstrumentor.instrument_app(app)
    
    return trace.get_tracer(__name__)
