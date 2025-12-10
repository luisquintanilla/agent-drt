import type { WorkflowRunSummary } from '../types';
import { StatusBadge } from './StatusBadge';
import './WorkflowList.css';

interface WorkflowListProps {
  workflows: WorkflowRunSummary[];
  selectedId?: string;
  isLoading: boolean;
  onSelect: (id: string) => void;
  onRefresh: () => void;
}

export function WorkflowList({
  workflows,
  selectedId,
  isLoading,
  onSelect,
  onRefresh,
}: WorkflowListProps) {
  return (
    <div className="workflow-list-card">
      <div className="list-header">
        <h2>Active Workflows</h2>
        <button className="btn btn-secondary" onClick={onRefresh} disabled={isLoading}>
          {isLoading ? <span className="spinner" /> : <RefreshIcon />}
          Refresh
        </button>
      </div>

      {workflows.length === 0 && !isLoading ? (
        <div className="empty-state">
          <EmptyIcon />
          <p>No workflows yet. Start a new workflow to begin!</p>
        </div>
      ) : (
        <div className="workflow-list">
          {workflows.map((workflow) => (
            <div
              key={workflow.id}
              className={`workflow-item ${selectedId === workflow.id ? 'selected' : ''}`}
              onClick={() => onSelect(workflow.id)}
            >
              <div className="workflow-item-header">
                <span className="workflow-name">{workflow.workflowName}</span>
                <StatusBadge status={workflow.status} />
              </div>
              <div className="workflow-item-meta">
                <span className="workflow-id">ID: {workflow.id.substring(0, 8)}...</span>
                <span className="workflow-date">
                  {new Date(workflow.createdAt).toLocaleString()}
                </span>
              </div>
              {workflow.pendingRequestCount > 0 && (
                <div className="pending-badge">
                  <AlertIcon />
                  <span>{workflow.pendingRequestCount} pending approval(s)</span>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function RefreshIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="23 4 23 10 17 10" />
      <path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10" />
    </svg>
  );
}

function EmptyIcon() {
  return (
    <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
      <line x1="9" y1="9" x2="15" y2="15" />
      <line x1="15" y1="9" x2="9" y2="15" />
    </svg>
  );
}

function AlertIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10" />
      <line x1="12" y1="16" x2="12" y2="12" />
      <line x1="12" y1="8" x2="12.01" y2="8" />
    </svg>
  );
}
