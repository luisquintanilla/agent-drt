import { useEffect, useRef, useCallback, useState } from 'react';
import type { MonitoringEvent } from '../types';

const BASE_URL = '/v1/monitor';
const RECONNECT_DELAY_MS = 3000;
const MAX_RECONNECT_ATTEMPTS = 5;

export interface UseMonitoringEventsOptions {
  /** Callback invoked when any event is received */
  onEvent?: (event: MonitoringEvent) => void;
  /** Whether SSE subscription is enabled */
  enabled?: boolean;
}

export interface UseMonitoringEventsReturn {
  /** Whether currently connected to the SSE stream */
  isConnected: boolean;
  /** Any connection error */
  error: Error | null;
  /** Manually reconnect */
  reconnect: () => void;
}

/**
 * Hook to subscribe to SSE monitoring events from the gateway.
 * Handles automatic reconnection on connection drops.
 */
export function useMonitoringEvents(
  options: UseMonitoringEventsOptions = {}
): UseMonitoringEventsReturn {
  const { onEvent, enabled = true } = options;
  
  const [isConnected, setIsConnected] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  
  const eventSourceRef = useRef<EventSource | null>(null);
  const reconnectAttemptsRef = useRef(0);
  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const onEventRef = useRef(onEvent);
  
  // Keep onEvent ref up to date without triggering reconnection
  useEffect(() => {
    onEventRef.current = onEvent;
  }, [onEvent]);

  const cleanup = useCallback(() => {
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = null;
    }
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
    setIsConnected(false);
  }, []);

  const connect = useCallback(() => {
    if (!enabled) {
      return;
    }

    cleanup();
    setError(null);

    const url = `${BASE_URL}/events`;
    const eventSource = new EventSource(url);
    eventSourceRef.current = eventSource;

    eventSource.onopen = () => {
      setIsConnected(true);
      setError(null);
      reconnectAttemptsRef.current = 0;
    };

    // Handle different event types
    const handleEvent = (eventType: string) => (event: MessageEvent) => {
      try {
        const payload = JSON.parse(event.data);
        const monitoringEvent: MonitoringEvent = {
          eventId: `${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
          eventType,
          timestamp: new Date().toISOString(),
          payload,
        };
        onEventRef.current?.(monitoringEvent);
      } catch (e) {
        console.error('Failed to parse SSE event:', e, event.data);
      }
    };

    // Listen for specific event types (must match backend MonitoringEventTypes)
    eventSource.addEventListener('worker.registered', handleEvent('worker.registered'));
    eventSource.addEventListener('worker.deregistered', handleEvent('worker.deregistered'));
    eventSource.addEventListener('worker.health_changed', handleEvent('worker.health_changed'));
    eventSource.addEventListener('worker.drained', handleEvent('worker.drained'));
    eventSource.addEventListener('worker.enabled', handleEvent('worker.enabled'));
    eventSource.addEventListener('workflow.started', handleEvent('workflow.started'));
    eventSource.addEventListener('workflow.completed', handleEvent('workflow.completed'));
    eventSource.addEventListener('workflow.failed', handleEvent('workflow.failed'));
    eventSource.addEventListener('workflow.cancelled', handleEvent('workflow.cancelled'));
    eventSource.addEventListener('workflow.aborted', handleEvent('workflow.aborted'));
    eventSource.addEventListener('workflow.waiting_for_signal', handleEvent('workflow.waiting_for_signal'));
    eventSource.addEventListener('workflow.signal_received', handleEvent('workflow.signal_received'));
    eventSource.addEventListener('workflow.step.started', handleEvent('workflow.step.started'));
    eventSource.addEventListener('workflow.step.completed', handleEvent('workflow.step.completed'));

    // Fallback for generic messages
    eventSource.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data) as MonitoringEvent;
        onEventRef.current?.(data);
      } catch (e) {
        console.error('Failed to parse SSE event:', e, event.data);
      }
    };

    eventSource.onerror = () => {
      setIsConnected(false);
      eventSource.close();
      eventSourceRef.current = null;

      // Attempt reconnection with exponential backoff
      if (reconnectAttemptsRef.current < MAX_RECONNECT_ATTEMPTS) {
        const delay = RECONNECT_DELAY_MS * Math.pow(2, reconnectAttemptsRef.current);
        reconnectAttemptsRef.current += 1;
        
        reconnectTimeoutRef.current = setTimeout(() => {
          connect();
        }, delay);
      } else {
        setError(new Error('Maximum reconnection attempts reached'));
      }
    };
  }, [enabled, cleanup]);

  const reconnect = useCallback(() => {
    reconnectAttemptsRef.current = 0;
    connect();
  }, [connect]);

  // Connect when enabled status changes
  useEffect(() => {
    if (enabled) {
      connect();
    } else {
      cleanup();
    }

    return cleanup;
  }, [enabled, connect, cleanup]);

  return {
    isConnected,
    error,
    reconnect,
  };
}
