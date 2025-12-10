import { useRef, useEffect, useState, useMemo } from 'react';
import type { MonitoringEvent, WorkerEventPayload, WorkflowEventPayload } from '../types';
import './EventFeedWidget.css';

type EventFilterType = 'all' | 'worker' | 'workflow';

interface EventFeedWidgetProps {
  events: MonitoringEvent[];
  maxEvents?: number;
  isConnected: boolean;
  connectionError: Error | null;
  onReconnect: () => void;
}

export function EventFeedWidget({
  events,
  maxEvents = 50,
  isConnected,
  connectionError,
  onReconnect,
}: EventFeedWidgetProps) {
  const listRef = useRef<HTMLDivElement>(null);
  const [filter, setFilter] = useState<EventFilterType>('all');
  const [autoScroll, setAutoScroll] = useState(true);

  // Filter events based on type
  const filteredEvents = useMemo(() => {
    if (filter === 'all') return events;
    return events.filter((e) =>
      filter === 'worker'
        ? e.eventType.toLowerCase().includes('worker')
        : e.eventType.toLowerCase().includes('workflow')
    );
  }, [events, filter]);

  const displayEvents = filteredEvents.slice(-maxEvents);

  // Auto-scroll to bottom when new events arrive
  useEffect(() => {
    if (autoScroll && listRef.current) {
      listRef.current.scrollTop = listRef.current.scrollHeight;
    }
  }, [events.length, autoScroll]);

  // Detect manual scroll to disable auto-scroll
  const handleScroll = () => {
    if (listRef.current) {
      const { scrollTop, scrollHeight, clientHeight } = listRef.current;
      const isAtBottom = scrollHeight - scrollTop - clientHeight < 20;
      setAutoScroll(isAtBottom);
    }
  };

  return (
    <div className="event-feed-widget">
      <div className="widget-header">
        <h2>Event Feed</h2>
        <div className="feed-controls">
          <div className="filter-buttons">
            <button
              className={`filter-btn ${filter === 'all' ? 'active' : ''}`}
              onClick={() => setFilter('all')}
            >
              All
            </button>
            <button
              className={`filter-btn ${filter === 'worker' ? 'active' : ''}`}
              onClick={() => setFilter('worker')}
            >
              Workers
            </button>
            <button
              className={`filter-btn ${filter === 'workflow' ? 'active' : ''}`}
              onClick={() => setFilter('workflow')}
            >
              Workflows
            </button>
          </div>
          <div className="connection-status">
            <span className={`status-dot ${isConnected ? 'connected' : 'disconnected'}`} />
            <span className="status-text">
              {isConnected ? 'Live' : 'Disconnected'}
            </span>
            {!isConnected && (
              <button onClick={onReconnect} className="reconnect-button">
                Reconnect
              </button>
            )}
          </div>
        </div>
      </div>

      {connectionError && (
        <div className="connection-error">
          Connection error: {connectionError.message}
        </div>
      )}

      <div className="event-list" ref={listRef} onScroll={handleScroll}>
        {displayEvents.length === 0 ? (
          <p className="no-events">
            {filter === 'all'
              ? 'No events yet. Waiting for activity...'
              : `No ${filter} events yet.`}
          </p>
        ) : (
          displayEvents.map((event) => (
            <EventItem key={event.eventId} event={event} />
          ))
        )}
      </div>

      <div className="event-footer">
        <span className="event-count">
          {filteredEvents.length === events.length
            ? `${events.length} event${events.length !== 1 ? 's' : ''}`
            : `${filteredEvents.length} of ${events.length} events`}
        </span>
        {!autoScroll && (
          <button
            className="scroll-to-bottom"
            onClick={() => {
              setAutoScroll(true);
              if (listRef.current) {
                listRef.current.scrollTop = listRef.current.scrollHeight;
              }
            }}
          >
            Scroll to latest
          </button>
        )}
      </div>
    </div>
  );
}

interface EventItemProps {
  event: MonitoringEvent;
}

function EventItem({ event }: EventItemProps) {
  const { icon, category } = getEventIconAndCategory(event.eventType);
  const details = formatEventDetails(event);

  return (
    <div className={`event-item ${category}`}>
      <span className="event-icon">{icon}</span>
      <div className="event-content">
        <div className="event-header">
          <span className="event-type">{formatEventType(event.eventType)}</span>
          <span className="event-time">{formatTimestamp(event.timestamp)}</span>
        </div>
        {details && <div className="event-details">{details}</div>}
      </div>
    </div>
  );
}

function getEventIconAndCategory(eventType: string): { icon: string; category: string } {
  const type = eventType.toLowerCase();
  
  // Worker events (format: worker.*)
  if (type.startsWith('worker.')) {
    if (type.includes('registered') && !type.includes('deregistered')) return { icon: '+', category: 'worker-add' };
    if (type.includes('deregistered')) return { icon: '-', category: 'worker-remove' };
    if (type.includes('health')) return { icon: 'H', category: 'worker-health' };
    if (type.includes('drain')) return { icon: 'D', category: 'worker-drain' };
    if (type.includes('enable')) return { icon: 'E', category: 'worker-enable' };
    return { icon: 'W', category: 'worker' };
  }

  // Workflow events (format: workflow.*)
  if (type.startsWith('workflow.')) {
    if (type.includes('started') && !type.includes('step')) return { icon: '>', category: 'workflow-start' };
    if (type.includes('completed') && !type.includes('step')) return { icon: 'v', category: 'workflow-complete' };
    if (type.includes('failed')) return { icon: 'x', category: 'workflow-fail' };
    if (type.includes('cancelled') || type.includes('aborted')) return { icon: 'x', category: 'workflow-fail' };
    if (type.includes('step')) return { icon: '.', category: 'workflow-step' };
    if (type.includes('signal') || type.includes('waiting')) return { icon: '!', category: 'workflow-signal' };
    return { icon: 'F', category: 'workflow' };
  }

  return { icon: '*', category: 'unknown' };
}

function formatEventType(eventType: string): string {
  // Convert dot.separated.lowercase to Title Case
  return eventType
    .split('.')
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

function formatTimestamp(isoDate: string): string {
  const date = new Date(isoDate);
  return date.toLocaleTimeString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

function formatEventDetails(event: MonitoringEvent): string | null {
  const payload = event.payload;
  if (!payload || typeof payload !== 'object') return null;

  const type = event.eventType.toLowerCase();

  // Worker events
  if (type.startsWith('worker.')) {
    const workerPayload = payload as WorkerEventPayload;
    if (workerPayload.workerId) {
      const parts = [`Worker: ${truncateId(workerPayload.workerId)}`];
      if (workerPayload.health) {
        parts.push(`Health: ${workerPayload.health}`);
      }
      return parts.join(' | ');
    }
  }

  // Workflow events
  if (type.startsWith('workflow.')) {
    const wfPayload = payload as WorkflowEventPayload;
    const parts: string[] = [];
    
    if (wfPayload.runId) {
      parts.push(`Run: ${truncateId(wfPayload.runId)}`);
    }
    if (wfPayload.workflowName) {
      parts.push(`Workflow: ${wfPayload.workflowName}`);
    }
    if (wfPayload.stepName) {
      parts.push(`Step: ${wfPayload.stepName}`);
    }
    if (wfPayload.status) {
      parts.push(`Status: ${wfPayload.status}`);
    }
    
    return parts.length > 0 ? parts.join(' | ') : null;
  }

  return null;
}

function truncateId(id: string, maxLength = 8): string {
  if (id.length <= maxLength) return id;
  return `${id.substring(0, maxLength)}...`;
}
