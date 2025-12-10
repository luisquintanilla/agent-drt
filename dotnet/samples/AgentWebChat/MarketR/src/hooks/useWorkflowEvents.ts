import { useEffect, useRef, useCallback, useState } from 'react';
import type { WorkflowStatusEvent } from '../types';

const BASE_URL = '/v1/workflows';
const RECONNECT_DELAY_MS = 3000;
const MAX_RECONNECT_ATTEMPTS = 5;

export interface UseWorkflowEventsOptions {
  /** Callback invoked when any event is received */
  onEvent?: (event: WorkflowStatusEvent) => void;
  /** Whether SSE subscription is enabled */
  enabled?: boolean;
}

export interface UseWorkflowEventsReturn {
  /** Whether currently connected to the SSE stream */
  isConnected: boolean;
  /** Any connection error */
  error: Error | null;
  /** Manually reconnect */
  reconnect: () => void;
}

/**
 * Hook to subscribe to SSE events for a specific workflow run.
 * Handles automatic reconnection on connection drops.
 */
export function useWorkflowEvents(
  runId: string | null | undefined,
  options: UseWorkflowEventsOptions = {}
): UseWorkflowEventsReturn {
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
    if (!runId || !enabled) {
      return;
    }

    cleanup();
    setError(null);

    const url = `${BASE_URL}/${encodeURIComponent(runId)}/events`;
    const eventSource = new EventSource(url);
    eventSourceRef.current = eventSource;

    eventSource.onopen = () => {
      setIsConnected(true);
      setError(null);
      reconnectAttemptsRef.current = 0;
    };

    eventSource.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data) as WorkflowStatusEvent;
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
  }, [runId, enabled, cleanup]);

  const reconnect = useCallback(() => {
    reconnectAttemptsRef.current = 0;
    connect();
  }, [connect]);

  // Connect when runId changes or enabled status changes
  useEffect(() => {
    if (enabled && runId) {
      connect();
    } else {
      cleanup();
    }

    return cleanup;
  }, [runId, enabled, connect, cleanup]);

  return {
    isConnected,
    error,
    reconnect,
  };
}
