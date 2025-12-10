import { useState, useEffect, useCallback } from 'react';
import {
  SystemStatusWidget,
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
  const [statusLoading, setStatusLoading] = useState(true);
  const [statusError, setStatusError] = useState<Error | null>(null);

  // Workers state
  const [workers, setWorkers] = useState<WorkerStatus[]>([]);
  const [workersLoading, setWorkersLoading] = useState(true);
  const [workersError, setWorkersError] = useState<Error | null>(null);

  // Workflows state
  const [activeWorkflows, setActiveWorkflows] = useState<WorkflowMonitoringSummary[]>([]);
  const [recentWorkflows, setRecentWorkflows] = useState<WorkflowMonitoringSummary[]>([]);
  const [workflowsLoading, setWorkflowsLoading] = useState(true);
  const [workflowsError, setWorkflowsError] = useState<Error | null>(null);

  // Selected workflow for detail modal
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string | null>(null);

  // Theme state
  const [isLightTheme, setIsLightTheme] = useState(false);

  // SSE events state
  const [events, setEvents] = useState<MonitoringEvent[]>([]);

  // Fetch functions
  const fetchSystemStatus = useCallback(async () => {
    try {
      setStatusLoading(true);
      setStatusError(null);
      const status = await monitoringApi.getSystemStatus();
      setSystemStatus(status);
    } catch (e) {
      setStatusError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setStatusLoading(false);
    }
  }, []);

  const fetchWorkers = useCallback(async () => {
    try {
      setWorkersLoading(true);
      setWorkersError(null);
      const data = await monitoringApi.getWorkers();
      setWorkers(data);
    } catch (e) {
      setWorkersError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setWorkersLoading(false);
    }
  }, []);

  const fetchWorkflows = useCallback(async () => {
    try {
      setWorkflowsLoading(true);
      setWorkflowsError(null);
      const [active, recent] = await Promise.all([
        monitoringApi.getActiveWorkflows(),
        monitoringApi.getRecentWorkflows(50),
      ]);
      setActiveWorkflows(active);
      setRecentWorkflows(recent);
    } catch (e) {
      setWorkflowsError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setWorkflowsLoading(false);
    }
  }, []);

  const refreshAll = useCallback(() => {
    fetchSystemStatus();
    fetchWorkers();
    fetchWorkflows();
  }, [fetchSystemStatus, fetchWorkers, fetchWorkflows]);

  // Handle SSE events
  const handleEvent = useCallback((event: MonitoringEvent) => {
    setEvents((prev) => [...prev.slice(-999), event]); // Keep last 1000 events

    // Refresh relevant data based on event type
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
    refreshAll();

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

  return (
    <div className={`app ${isLightTheme ? 'theme-light' : ''}`}>
      <header className="app-header">
        <h1>Workflow Monitor</h1>
        <div className="header-actions">
          <div className="connection-indicator">
            <span className={`indicator-dot ${isConnected ? 'connected' : 'disconnected'}`} />
            <span className="indicator-text">{isConnected ? 'Live' : 'Disconnected'}</span>
            {!isConnected && (
              <button onClick={reconnect} className="reconnect-btn">Retry</button>
            )}
          </div>
          <button onClick={toggleTheme} className="theme-toggle" title="Toggle theme (T)">
            {isLightTheme ? 'Dark' : 'Light'}
          </button>
          <button onClick={refreshAll} className="refresh-all-button" title="Refresh all (R)">
            Refresh
          </button>
        </div>
      </header>

      <main className="dashboard">
        <section className="dashboard-row status-row">
          <SystemStatusWidget
            status={systemStatus}
            isLoading={statusLoading}
            error={statusError}
          />
        </section>

        <section className="dashboard-row main-row">
          <div className="column workers-column">
            <WorkerListWidget
              workers={workers}
              isLoading={workersLoading}
              error={workersError}
              onRefresh={fetchWorkers}
            />
          </div>

          <div className="column workflows-column">
            <WorkflowsWidget
              activeWorkflows={activeWorkflows}
              recentWorkflows={recentWorkflows}
              isLoading={workflowsLoading}
              error={workflowsError}
              onRefresh={fetchWorkflows}
              onSelectWorkflow={handleSelectWorkflow}
            />
          </div>
        </section>

        <section className="dashboard-row events-row">
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
        />
      )}
    </div>
  );
}
