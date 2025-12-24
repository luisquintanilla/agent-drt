# Copyright (c) Microsoft. All rights reserved.

import logging
import os
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


def configure_telemetry(app, service_name: str = "python-agent"):
    """
    Configure OpenTelemetry for FastAPI application.
    
    This function sets up tracing, metrics, and logging to export to the Aspire dashboard
    via OTLP (OpenTelemetry Protocol).
    
    Args:
        app: The FastAPI application instance
        service_name: The name to identify this service in telemetry data
        
    Returns:
        A tracer instance for creating custom spans
    """
    # Get OTLP endpoint from environment or use default for standalone dashboard
    # Aspire provides OTEL_EXPORTER_OTLP_ENDPOINT automatically
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
    
    # Add logging handler to root logger
    handler = LoggingHandler(
        level=logging.NOTSET,
        logger_provider=logger_provider
    )
    logging.getLogger().addHandler(handler)
    
    # Instrument FastAPI application
    FastAPIInstrumentor.instrument_app(app)
    
    # Instrument Pydantic AI for OpenTelemetry Gen AI Semantic Conventions
    # This will automatically trace all agent runs with standard gen_ai.* attributes
    from pydantic_ai import Agent, InstrumentationSettings
    
    # Use version 3 for full OpenTelemetry Gen AI semantic conventions compliance
    # This generates spans like "invoke_agent {agent_name}" with standard attributes:
    # - gen_ai.operation.name, gen_ai.system, gen_ai.request.model
    # - gen_ai.usage.input_tokens, gen_ai.usage.output_tokens
    # - gen_ai.input.messages, gen_ai.output.messages
    instrumentation_settings = InstrumentationSettings(
        version=3,  # Full semantic conventions compliance
        include_content=True,  # Include prompts and completions in traces
        include_binary_content=False,  # Don't include images/audio to reduce trace size
    )
    Agent.instrument_all(instrumentation_settings)
    
    return trace.get_tracer(__name__)
