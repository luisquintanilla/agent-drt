# Copyright (c) Microsoft. All rights reserved.

import logging
import os
from opentelemetry import metrics, trace
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
    
    return trace.get_tracer(__name__)
