import { useState } from 'react';
import type { WorkerStatus, WorkerHealthState } from '../types';
import { monitoringApi } from '../api';
import { Skeleton } from './Skeleton';
import './WorkerListWidget.css';

interface WorkerListWidgetProps {
  workers: WorkerStatus[];
  isLoading: boolean;
  error: Error | null;
  onRefresh: () => void;
}

export function WorkerListWidget({ workers, isLoading, error, onRefresh }: WorkerListWidgetProps) {
  const [actionInProgress, setActionInProgress] = useState<string | null>(null);

  const handleDrain = async (workerId: string) => {
    setActionInProgress(workerId);
    try {
      await monitoringApi.drainWorker(workerId);
      onRefresh();
    } catch (e) {
      console.error('Failed to drain worker:', e);
    } finally {
      setActionInProgress(null);
    }
  };

  const handleEnable = async (workerId: string) => {
    setActionInProgress(workerId);
    try {
      await monitoringApi.enableWorker(workerId);
      onRefresh();
    } catch (e) {
      console.error('Failed to enable worker:', e);
    } finally {
      setActionInProgress(null);
    }
  };

  if (isLoading) {
    return (
      <div className="worker-list-widget">
        <div className="widget-header">
          <h2>Workers</h2>
          <button className="refresh-button" disabled>Refresh</button>
        </div>
        <div className="worker-list">
          {[1, 2].map((i) => (
            <div key={i} className="worker-card skeleton-card">
              <div className="worker-header">
                <Skeleton variant="text" width={100} height={16} />
                <Skeleton variant="badge" />
              </div>
              <div className="worker-details">
                <div className="detail-row">
                  <span className="label">Address:</span>
                  <Skeleton variant="text" width={120} height={14} />
                </div>
                <div className="detail-row">
                  <span className="label">Active Workflows:</span>
                  <Skeleton variant="text" width={30} height={14} />
                </div>
                <div className="detail-row">
                  <span className="label">Last Health Check:</span>
                  <Skeleton variant="text" width={60} height={14} />
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="worker-list-widget error">
        <h2>Workers</h2>
        <p className="error-message">Failed to load: {error.message}</p>
        <button onClick={onRefresh} className="retry-button">Retry</button>
      </div>
    );
  }

  return (
    <div className="worker-list-widget">
      <div className="widget-header">
        <h2>Workers</h2>
        <button onClick={onRefresh} className="refresh-button" title="Refresh">
          Refresh
        </button>
      </div>
      
      {workers.length === 0 ? (
        <p className="no-data">No workers registered</p>
      ) : (
        <div className="worker-list">
          {workers.map((worker) => (
            <WorkerCard
              key={worker.workerId}
              worker={worker}
              isActionInProgress={actionInProgress === worker.workerId}
              onDrain={() => handleDrain(worker.workerId)}
              onEnable={() => handleEnable(worker.workerId)}
            />
          ))}
        </div>
      )}
    </div>
  );
}

interface WorkerCardProps {
  worker: WorkerStatus;
  isActionInProgress: boolean;
  onDrain: () => void;
  onEnable: () => void;
}

function WorkerCard({ worker, isActionInProgress, onDrain, onEnable }: WorkerCardProps) {
  return (
    <div className={`worker-card ${getHealthClass(worker.health)} ${worker.isDraining ? 'draining' : ''}`}>
      <div className="worker-header">
        <span className="worker-id" title={worker.workerId}>
          {truncateId(worker.workerId)}
        </span>
        <span className={`health-badge ${getHealthClass(worker.health)}`}>
          {worker.health}
        </span>
      </div>
      
      <div className="worker-details">
        <div className="detail-row">
          <span className="label">Address:</span>
          <span className="value">{worker.address}</span>
        </div>
        <div className="detail-row">
          <span className="label">Active Workflows:</span>
          <span className="value">{worker.activeWorkflows}</span>
        </div>
        <div className="detail-row">
          <span className="label">Last Health Check:</span>
          <span className="value">{formatTimeAgo(worker.lastHealthCheck)}</span>
        </div>
        {worker.isDraining && (
          <div className="detail-row draining-status">
            <span className="label">Status:</span>
            <span className="value draining">Draining</span>
          </div>
        )}
      </div>

      {worker.supportedWorkflows.length > 0 && (
        <div className="supported-items">
          <span className="label">Workflows:</span>
          <div className="tag-list">
            {worker.supportedWorkflows.map((wf) => (
              <span key={wf} className="tag workflow-tag">{wf}</span>
            ))}
          </div>
        </div>
      )}

      {worker.supportedAgents.length > 0 && (
        <div className="supported-items">
          <span className="label">Agents:</span>
          <div className="tag-list">
            {worker.supportedAgents.map((agent) => (
              <span key={agent} className="tag agent-tag">{agent}</span>
            ))}
          </div>
        </div>
      )}

      <div className="worker-actions">
        {worker.isDraining ? (
          <button
            onClick={onEnable}
            disabled={isActionInProgress}
            className="action-button enable"
          >
            {isActionInProgress ? 'Enabling...' : 'Enable'}
          </button>
        ) : (
          <button
            onClick={onDrain}
            disabled={isActionInProgress}
            className="action-button drain"
          >
            {isActionInProgress ? 'Draining...' : 'Drain'}
          </button>
        )}
      </div>
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

function truncateId(id: string, maxLength = 12): string {
  if (id.length <= maxLength) return id;
  return `${id.substring(0, maxLength)}...`;
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
