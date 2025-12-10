import type { WorkflowMonitoringSummary } from '../types';
import './ActiveWorkflowsWidget.css';

interface ActiveWorkflowsWidgetProps {
  workflows: WorkflowMonitoringSummary[];
  isLoading: boolean;
  error: Error | null;
  onRefresh: () => void;
}

export function ActiveWorkflowsWidget({ workflows, isLoading, error, onRefresh }: ActiveWorkflowsWidgetProps) {
  if (isLoading) {
    return (
      <div className="active-workflows-widget loading">
        <h2>Active Workflows</h2>
        <p>Loading...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="active-workflows-widget error">
        <h2>Active Workflows</h2>
        <p className="error-message">Failed to load: {error.message}</p>
        <button onClick={onRefresh} className="retry-button">Retry</button>
      </div>
    );
  }

  return (
    <div className="active-workflows-widget">
      <div className="widget-header">
        <h2>Active Workflows</h2>
        <button onClick={onRefresh} className="refresh-button" title="Refresh">
          Refresh
        </button>
      </div>

      {workflows.length === 0 ? (
        <p className="no-data">No active workflows</p>
      ) : (
        <div className="workflow-list">
          <table className="workflow-table">
            <thead>
              <tr>
                <th>Run ID</th>
                <th>Workflow</th>
                <th>Status</th>
                <th>Started</th>
                <th>Signal</th>
              </tr>
            </thead>
            <tbody>
              {workflows.map((wf) => (
                <tr key={wf.runId} className={`status-${getStatusClass(wf.status)}`}>
                  <td className="run-id" title={wf.runId}>
                    {truncateId(wf.runId)}
                  </td>
                  <td className="workflow-name">{wf.workflowName}</td>
                  <td>
                    <span className={`status-badge ${getStatusClass(wf.status)}`}>
                      {wf.status}
                    </span>
                  </td>
                  <td className="time">{formatTime(wf.createdAt)}</td>
                  <td className="signal">
                    {wf.hasPendingSignal && (
                      <span className="pending-signal" title="Waiting for signal">
                        Pending
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function getStatusClass(status: string): string {
  const lower = status.toLowerCase();
  if (lower.includes('running') || lower.includes('executing')) return 'running';
  if (lower.includes('completed') || lower.includes('success')) return 'completed';
  if (lower.includes('failed') || lower.includes('error')) return 'failed';
  if (lower.includes('waiting') || lower.includes('pending')) return 'waiting';
  if (lower.includes('cancelled') || lower.includes('canceled')) return 'cancelled';
  return 'unknown';
}

function truncateId(id: string, maxLength = 12): string {
  if (id.length <= maxLength) return id;
  return `${id.substring(0, maxLength)}...`;
}

function formatTime(isoDate: string): string {
  const date = new Date(isoDate);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffSeconds = Math.floor(diffMs / 1000);
  
  if (diffSeconds < 60) {
    return `${diffSeconds}s ago`;
  }
  
  const diffMinutes = Math.floor(diffSeconds / 60);
  if (diffMinutes < 60) {
    return `${diffMinutes}m ago`;
  }
  
  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) {
    return `${diffHours}h ${diffMinutes % 60}m ago`;
  }
  
  // For older items, show the date
  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}
