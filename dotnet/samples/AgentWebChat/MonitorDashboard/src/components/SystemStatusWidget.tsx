import type { SystemStatus } from '../types';
import { SkeletonMetricRow } from './Skeleton';
import './SystemStatusWidget.css';

interface SystemStatusWidgetProps {
  status: SystemStatus | null;
  isLoading: boolean;
  error: Error | null;
}

export function SystemStatusWidget({ status, isLoading, error }: SystemStatusWidgetProps) {
  if (isLoading) {
    return (
      <div className="system-status-widget">
        <h2>System Status</h2>
        <div className="status-grid">
          <div className="status-section">
            <h3>Workflows</h3>
            <SkeletonMetricRow label="Active" />
            <SkeletonMetricRow label="Queued" />
            <SkeletonMetricRow label="Waiting for Signal" />
            <SkeletonMetricRow label="Completed (24h)" />
            <SkeletonMetricRow label="Failed (24h)" />
          </div>
          <div className="status-section">
            <h3>Workers</h3>
            <SkeletonMetricRow label="Registered" />
            <SkeletonMetricRow label="Healthy" />
            <SkeletonMetricRow label="Drained" />
          </div>
          <div className="status-section">
            <h3>System</h3>
            <SkeletonMetricRow label="Uptime" />
            <SkeletonMetricRow label="Version" />
          </div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="system-status-widget error">
        <h2>System Status</h2>
        <p className="error-message">Failed to load: {error.message}</p>
      </div>
    );
  }

  if (!status) {
    return null;
  }

  return (
    <div className="system-status-widget">
      <h2>System Status</h2>
      <div className="status-grid">
        <div className="status-section workflows">
          <h3>Workflows</h3>
          <div className="metric">
            <span className="label">Active</span>
            <span className="value running">{status.activeWorkflows}</span>
          </div>
          <div className="metric">
            <span className="label">Queued</span>
            <span className="value queued">{status.queuedWorkflows}</span>
          </div>
          <div className="metric">
            <span className="label">Waiting for Signal</span>
            <span className="value waiting">{status.waitingForSignalWorkflows}</span>
          </div>
          <div className="metric">
            <span className="label">Completed (24h)</span>
            <span className="value completed">{status.completedWorkflows24h}</span>
          </div>
          <div className="metric">
            <span className="label">Failed (24h)</span>
            <span className="value failed">{status.failedWorkflows24h}</span>
          </div>
        </div>
        
        <div className="status-section workers">
          <h3>Workers</h3>
          <div className="metric">
            <span className="label">Registered</span>
            <span className="value">{status.registeredWorkers}</span>
          </div>
          <div className="metric">
            <span className="label">Healthy</span>
            <span className="value healthy">{status.healthyWorkers}</span>
          </div>
          <div className="metric">
            <span className="label">Drained</span>
            <span className="value drained">{status.drainedWorkers}</span>
          </div>
        </div>

        <div className="status-section system">
          <h3>System</h3>
          <div className="metric">
            <span className="label">Uptime</span>
            <span className="value">{formatUptime(status.uptime)}</span>
          </div>
          {status.version && (
            <div className="metric">
              <span className="label">Version</span>
              <span className="value">{status.version}</span>
            </div>
          )}
        </div>
      </div>
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
  if (minutes > 0) parts.push(`${minutes}m`);
  
  return parts.length > 0 ? parts.join(' ') : '< 1m';
}
