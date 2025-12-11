import { useState } from 'react';
import type { WorkerStatus, WorkerHealthState } from '../types';
import { monitoringApi } from '../api';
import { Skeleton } from './Skeleton';
import './WorkerListWidget.css';

interface WorkerStats {
  registered: number;
  healthy: number;
  drained: number;
}

interface WorkerListWidgetProps {
  workers: WorkerStatus[];
  stats: WorkerStats | null;
  isLoading: boolean;
  error: Error | null;
  onRefresh: () => void;
}

export function WorkerListWidget({ workers, stats, isLoading, error, onRefresh }: WorkerListWidgetProps) {
  const [actionInProgress, setActionInProgress] = useState<string | null>(null);
  const [expandedWorkerId, setExpandedWorkerId] = useState<string | null>(null);

  const handleDrain = async (e: React.MouseEvent, workerId: string) => {
    e.stopPropagation();
    setActionInProgress(workerId);
    try {
      await monitoringApi.drainWorker(workerId);
      onRefresh();
    } catch (err) {
      console.error('Failed to drain worker:', err);
    } finally {
      setActionInProgress(null);
    }
  };

  const handleEnable = async (e: React.MouseEvent, workerId: string) => {
    e.stopPropagation();
    setActionInProgress(workerId);
    try {
      await monitoringApi.enableWorker(workerId);
      onRefresh();
    } catch (err) {
      console.error('Failed to enable worker:', err);
    } finally {
      setActionInProgress(null);
    }
  };

  const toggleExpand = (workerId: string) => {
    setExpandedWorkerId(expandedWorkerId === workerId ? null : workerId);
  };

  if (isLoading) {
    return (
      <div className="worker-list-widget">
        <div className="widget-header">
          <div className="header-title-row">
            <h2>Workers</h2>
            <div className="header-stats">
              <Skeleton variant="text" width={80} height={16} />
            </div>
          </div>
        </div>
        <div className="worker-table-container">
          <table className="worker-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Status</th>
                <th>Active</th>
                <th>Last Check</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {[1, 2, 3].map((i) => (
                <tr key={i}>
                  <td><Skeleton variant="text" width={80} height={14} /></td>
                  <td><Skeleton variant="badge" /></td>
                  <td><Skeleton variant="text" width={24} height={14} /></td>
                  <td><Skeleton variant="text" width={50} height={14} /></td>
                  <td><Skeleton variant="text" width={50} height={24} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="worker-list-widget error-state">
        <div className="widget-header">
          <h2>Workers</h2>
          <button onClick={onRefresh} className="refresh-button">Retry</button>
        </div>
        <p className="error-message">Failed to load: {error.message}</p>
      </div>
    );
  }

  return (
    <div className="worker-list-widget">
      <div className="widget-header">
        <h2>Workers</h2>
        {stats && (
          <div className="header-stats">
            <span className="stat healthy" title="Healthy workers">
              <span className="stat-value">{stats.healthy}</span>
              <span className="stat-label">healthy</span>
            </span>
            <span className="stat-divider">/</span>
            <span className="stat" title="Total registered workers">
              <span className="stat-value">{stats.registered}</span>
              <span className="stat-label">total</span>
            </span>
            {stats.drained > 0 && (
              <>
                <span className="stat-divider">|</span>
                <span className="stat drained" title="Drained workers">
                  <span className="stat-value">{stats.drained}</span>
                  <span className="stat-label">drained</span>
                </span>
              </>
            )}
          </div>
        )}
      </div>
      
      {workers.length === 0 ? (
        <p className="no-data">No workers registered</p>
      ) : (
        <div className="worker-table-container">
          <table className="worker-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Status</th>
                <th>Active</th>
                <th>Last Check</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {workers.map((worker) => (
                <>
                  <tr 
                    key={worker.workerId}
                    className={`worker-row ${getHealthClass(worker.health)} ${worker.isDraining ? 'draining' : ''} ${expandedWorkerId === worker.workerId ? 'expanded' : ''}`}
                    onClick={() => toggleExpand(worker.workerId)}
                  >
                    <td className="worker-id-cell">
                      <span className={`health-indicator ${getHealthClass(worker.health)}`}></span>
                      <span className="worker-id" title={worker.workerId}>
                        {worker.workerId}
                      </span>
                    </td>
                    <td>
                      <span className={`status-badge ${getHealthClass(worker.health)}`}>
                        {worker.isDraining ? 'Draining' : worker.health}
                      </span>
                    </td>
                    <td className="active-count">{worker.activeWorkflows}</td>
                    <td className="last-check">{formatTimeAgo(worker.lastHealthCheck)}</td>
                    <td className="actions-cell">
                      {worker.isDraining ? (
                        <button
                          onClick={(e) => handleEnable(e, worker.workerId)}
                          disabled={actionInProgress === worker.workerId}
                          className="action-btn enable"
                        >
                          {actionInProgress === worker.workerId ? '...' : 'Enable'}
                        </button>
                      ) : (
                        <button
                          onClick={(e) => handleDrain(e, worker.workerId)}
                          disabled={actionInProgress === worker.workerId}
                          className="action-btn drain"
                        >
                          {actionInProgress === worker.workerId ? '...' : 'Drain'}
                        </button>
                      )}
                    </td>
                  </tr>
                  {expandedWorkerId === worker.workerId && (
                    <tr key={`${worker.workerId}-details`} className="worker-details-row">
                      <td colSpan={5}>
                        <div className="worker-details-content">
                          <div className="detail-item">
                            <span className="detail-label">Address:</span>
                            <span className="detail-value">{worker.address}</span>
                          </div>
                          {worker.supportedWorkflows.length > 0 && (
                            <div className="detail-item">
                              <span className="detail-label">Workflows:</span>
                              <div className="tag-list">
                                {worker.supportedWorkflows.map((wf) => (
                                  <span key={wf} className="tag workflow-tag">{wf}</span>
                                ))}
                              </div>
                            </div>
                          )}
                          {worker.supportedAgents.length > 0 && (
                            <div className="detail-item">
                              <span className="detail-label">Agents:</span>
                              <div className="tag-list">
                                {worker.supportedAgents.map((agent) => (
                                  <span key={agent} className="tag agent-tag">{agent}</span>
                                ))}
                              </div>
                            </div>
                          )}
                        </div>
                      </td>
                    </tr>
                  )}
                </>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function getHealthClass(health: WorkerHealthState): string {
  switch (health) {
    case 'Healthy':
      return 'healthy';
    case 'Unhealthy':
      return 'unhealthy';
    case 'Degraded':
      return 'degraded';
    default:
      return 'unknown';
  }
}

function formatTimeAgo(isoDate: string): string {
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
    return `${diffHours}h ago`;
  }
  
  const diffDays = Math.floor(diffHours / 24);
  return `${diffDays}d ago`;
}
