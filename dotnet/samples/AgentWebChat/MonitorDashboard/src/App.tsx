import { useState, useEffect, useCallback, useRef } from 'react';
import {
  WorkerListWidget,
  WorkflowsWidget,
  WorkflowDetailModal,
  EventFeedWidget,
} from './components';
import { useMonitoringEvents } from './hooks';
import { monitoringApi } from './api';
import type {
  SystemStatus,
  WorkerStatus,
  WorkflowMonitoringSummary,
  MonitoringEvent,
} from './types';
import './App.css';

const REFRESH_INTERVAL_MS = 10000; // 10 seconds

export default function App() {
  // System status state
  const [systemStatus, setSystemStatus] = useState<SystemStatus | null>(null);

  // Workers state
  const [workers, setWorkers] = useState<WorkerStatus[]>([]);
  const [workersError, setWorkersError] = useState<Error | null>(null);

  // Workflows state
  const [activeWorkflows, setActiveWorkflows] = useState<WorkflowMonitoringSummary[]>([]);
  const [recentWorkflows, setRecentWorkflows] = useState<WorkflowMonitoringSummary[]>([]);
  const [workflowsError, setWorkflowsError] = useState<Error | null>(null);

  // Track initial load state (only show skeletons on first load)
  const [initialLoadComplete, setInitialLoadComplete] = useState(false);

  // Selected workflow for detail modal
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string | null>(null);

  // Theme state
  const [isLightTheme, setIsLightTheme] = useState(false);

  // SSE events state
  const [events, setEvents] = useState<MonitoringEvent[]>([]);

  // Track if initial fetch has been done
  const initialFetchDone = useRef(false);

  // Fetch functions - these update data silently in the background
  const fetchSystemStatus = useCallback(async () => {
    try {
      const status = await monitoringApi.getSystemStatus();
      setSystemStatus(status);
    } catch (e) {
      console.error('Failed to fetch system status:', e);
    }
  }, []);

  const fetchWorkers = useCallback(async () => {
    try {
      setWorkersError(null);
      const data = await monitoringApi.getWorkers();
      setWorkers(data);
    } catch (e) {
      setWorkersError(e instanceof Error ? e : new Error(String(e)));
    }
  }, []);

  const fetchWorkflows = useCallback(async () => {
    try {
      setWorkflowsError(null);
      const [active, recent] = await Promise.all([
        monitoringApi.getActiveWorkflows(),
        monitoringApi.getRecentWorkflows(50),
      ]);
      setActiveWorkflows(active);
      setRecentWorkflows(recent);
    } catch (e) {
      setWorkflowsError(e instanceof Error ? e : new Error(String(e)));
    }
  }, []);

  // Refresh all data silently in the background
  const refreshAll = useCallback(async () => {
    await Promise.all([
      fetchSystemStatus(),
      fetchWorkers(),
      fetchWorkflows(),
    ]);
  }, [fetchSystemStatus, fetchWorkers, fetchWorkflows]);

  // Handle SSE events - refresh data silently
  const handleEvent = useCallback((event: MonitoringEvent) => {
    setEvents((prev) => [...prev.slice(-999), event]); // Keep last 1000 events

    // Refresh relevant data based on event type (silently in background)
    if (event.eventType.includes('Worker')) {
      fetchWorkers();
      fetchSystemStatus();
    }
    if (event.eventType.includes('Workflow')) {
      fetchWorkflows();
      fetchSystemStatus();
    }
  }, [fetchWorkers, fetchWorkflows, fetchSystemStatus]);

  // SSE connection
  const { isConnected, error: connectionError, reconnect } = useMonitoringEvents({
    onEvent: handleEvent,
    enabled: true,
  });

  // Initial fetch and periodic refresh
  useEffect(() => {
    if (!initialFetchDone.current) {
      initialFetchDone.current = true;
      // Initial load - then mark as complete
      refreshAll().then(() => {
        setInitialLoadComplete(true);
      });
    }

    const interval = setInterval(refreshAll, REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [refreshAll]);

  const handleSelectWorkflow = useCallback((runId: string) => {
    setSelectedWorkflowId(runId);
  }, []);

  const handleCloseModal = useCallback(() => {
    setSelectedWorkflowId(null);
  }, []);

  const toggleTheme = useCallback(() => {
    setIsLightTheme((prev) => !prev);
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ignore if typing in an input
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) {
        return;
      }

      switch (e.key.toLowerCase()) {
        case 'r':
          // Refresh all data
          refreshAll();
          break;
        case 'escape':
          // Close modal
          if (selectedWorkflowId) {
            handleCloseModal();
          }
          break;
        case 't':
          // Toggle theme
          toggleTheme();
          break;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [refreshAll, selectedWorkflowId, handleCloseModal, toggleTheme]);

  // Extract stats for widgets
  const workerStats = systemStatus ? {
    registered: systemStatus.registeredWorkers,
    healthy: systemStatus.healthyWorkers,
    drained: systemStatus.drainedWorkers,
  } : null;

  const workflowStats = systemStatus ? {
    active: systemStatus.activeWorkflows,
    queued: systemStatus.queuedWorkflows,
    waiting: systemStatus.waitingForSignalWorkflows,
    completed24h: systemStatus.completedWorkflows24h,
    failed24h: systemStatus.failedWorkflows24h,
  } : null;

  const uptime = systemStatus?.uptime ? formatUptime(systemStatus.uptime) : null;

  return (
    <div className={`app ${isLightTheme ? 'theme-light' : ''}`}>
      <header className="app-header">
        <div className="header-left">
          <h1>Workflow Monitor</h1>
          {uptime && (
            <span className="uptime-badge" title="System uptime">
              <span className="uptime-icon">^</span>
              {uptime}
            </span>
          )}
        </div>
        <div className="header-right">
          <button onClick={toggleTheme} className="theme-toggle" title="Toggle theme (T)">
            {isLightTheme ? (
              <svg className="theme-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
              </svg>
            ) : (
              <svg className="theme-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <circle cx="12" cy="12" r="5" />
                <line x1="12" y1="1" x2="12" y2="3" />
                <line x1="12" y1="21" x2="12" y2="23" />
                <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
                <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
                <line x1="1" y1="12" x2="3" y2="12" />
                <line x1="21" y1="12" x2="23" y2="12" />
                <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
                <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
              </svg>
            )}
          </button>
          <div className="connection-status">
            <span className={`status-dot ${isConnected ? 'connected' : 'disconnected'}`} />
            <span className="status-text">{isConnected ? 'Live' : 'Disconnected'}</span>
            {!isConnected && (
              <button onClick={reconnect} className="reconnect-btn">Retry</button>
            )}
          </div>
        </div>
      </header>

      <main className="dashboard">
        {/* Main Content - Two Panel Layout */}
        <section className="main-content">
          <div className="panel workers-panel">
            <WorkerListWidget
              workers={workers}
              stats={workerStats}
              isLoading={!initialLoadComplete}
              error={workersError}
              onRefresh={fetchWorkers}
            />
          </div>

          <div className="panel workflows-panel">
            <WorkflowsWidget
              activeWorkflows={activeWorkflows}
              recentWorkflows={recentWorkflows}
              stats={workflowStats}
              isLoading={!initialLoadComplete}
              error={workflowsError}
              onRefresh={fetchWorkflows}
              onSelectWorkflow={handleSelectWorkflow}
            />
          </div>
        </section>

        {/* Event Feed */}
        <section className="events-section">
          <EventFeedWidget
            events={events}
            isConnected={isConnected}
            connectionError={connectionError}
            onReconnect={reconnect}
          />
        </section>
      </main>

      {selectedWorkflowId && (
        <WorkflowDetailModal
          runId={selectedWorkflowId}
          onClose={handleCloseModal}
          onWorkflowUpdated={refreshAll}
        />
      )}
    </div>
  );
}

function formatUptime(uptime: string): string {
  // Parse TimeSpan format like "1.02:03:04.5"
  const match = uptime.match(/^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/);
  if (!match) return uptime;

  const days = match[1] ? parseInt(match[1]) : 0;
  const hours = parseInt(match[2]);
  const minutes = parseInt(match[3]);

  const parts: string[] = [];
  if (days > 0) parts.push(`${days}d`);
  if (hours > 0) parts.push(`${hours}h`);
  if (minutes > 0 || parts.length === 0) parts.push(`${minutes}m`);
  
  return parts.join(' ');
}
