import type { SystemStatus, WorkerStatus, WorkflowMonitoringSummary, MonitoringEvent } from '../types';
import { WorkerListWidget, WorkflowsWidget, EventFeedWidget, WorkflowDetailModal } from '../components';
import './DashboardPage.css';

interface DashboardPageProps {
  systemStatus: SystemStatus | null;
  workers: WorkerStatus[];
  workersError: Error | null;
  activeWorkflows: WorkflowMonitoringSummary[];
  recentWorkflows: WorkflowMonitoringSummary[];
  workflowsError: Error | null;
  isLoading: boolean;
  onRefreshWorkers: () => void;
  onRefreshWorkflows: () => void;
  selectedWorkflowId: string | null;
  onSelectWorkflow: (runId: string) => void;
  onCloseModal: () => void;
  onWorkflowUpdated: () => void;
  events: MonitoringEvent[];
  isConnected: boolean;
  connectionError: Error | null;
  onReconnect: () => void;
  uptime: string | null;
}

export function DashboardPage({
  systemStatus,
  workers,
  workersError,
  activeWorkflows,
  recentWorkflows,
  workflowsError,
  isLoading,
  onRefreshWorkers,
  onRefreshWorkflows,
  selectedWorkflowId,
  onSelectWorkflow,
  onCloseModal,
  onWorkflowUpdated,
  events,
  isConnected,
  connectionError,
  onReconnect,
  uptime,
}: DashboardPageProps) {
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

  return (
    <div className="dashboard-page">
      <header className="page-header">
        <div className="page-header-content">
          <h1>Dashboard</h1>
          <p className="page-description">Overview of your agent runtime</p>
        </div>
        {uptime && (
          <div className="uptime-display">
            <span className="uptime-label">Uptime</span>
            <span className="uptime-value">{uptime}</span>
          </div>
        )}
      </header>

      <div className="dashboard-content">
        <section className="main-panels">
          <div className="panel workers-panel">
            <WorkerListWidget
              workers={workers}
              stats={workerStats}
              isLoading={isLoading}
              error={workersError}
              onRefresh={onRefreshWorkers}
            />
          </div>

          <div className="panel workflows-panel">
            <WorkflowsWidget
              activeWorkflows={activeWorkflows}
              recentWorkflows={recentWorkflows}
              stats={workflowStats}
              isLoading={isLoading}
              error={workflowsError}
              onRefresh={onRefreshWorkflows}
              onSelectWorkflow={onSelectWorkflow}
            />
          </div>
        </section>

        <section className="events-section">
          <EventFeedWidget
            events={events}
            isConnected={isConnected}
            connectionError={connectionError}
            onReconnect={onReconnect}
          />
        </section>
      </div>

      {selectedWorkflowId && (
        <WorkflowDetailModal
          runId={selectedWorkflowId}
          onClose={onCloseModal}
          onWorkflowUpdated={onWorkflowUpdated}
        />
      )}
    </div>
  );
}
