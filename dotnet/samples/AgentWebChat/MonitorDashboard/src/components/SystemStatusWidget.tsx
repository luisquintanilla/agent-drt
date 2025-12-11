import type { SystemStatus } from '../types';
import { Skeleton } from './Skeleton';
import './SystemStatusWidget.css';

interface SystemStatusWidgetProps {
  status: SystemStatus | null;
  isLoading: boolean;
  error: Error | null;
}

export function SystemStatusWidget({ status, isLoading, error }: SystemStatusWidgetProps) {
  if (isLoading) {
    return (
      <div className="kpi-cards">
        {[1, 2, 3, 4, 5, 6].map((i) => (
          <div key={i} className="kpi-card">
            <Skeleton variant="text" width={40} height={28} />
            <Skeleton variant="text" width={70} height={12} />
          </div>
        ))}
      </div>
    );
  }

  if (error) {
    return (
      <div className="kpi-cards">
        <div className="kpi-card error">
          <span className="kpi-value">!</span>
          <span className="kpi-label">Error loading status</span>
        </div>
      </div>
    );
  }

  if (!status) {
    return null;
  }

  return (
    <div className="kpi-cards">
      <div className="kpi-card">
        <span className="kpi-value running">{status.activeWorkflows}</span>
        <span className="kpi-label">Active</span>
      </div>
      <div className="kpi-card">
        <span className="kpi-value queued">{status.queuedWorkflows}</span>
        <span className="kpi-label">Queued</span>
      </div>
      <div className="kpi-card">
        <span className="kpi-value waiting">{status.waitingForSignalWorkflows}</span>
        <span className="kpi-label">Waiting</span>
      </div>
      <div className="kpi-card">
        <span className="kpi-value completed">{status.completedWorkflows24h}</span>
        <span className="kpi-label">Completed 24h</span>
      </div>
      <div className="kpi-card">
        <span className="kpi-value failed">{status.failedWorkflows24h}</span>
        <span className="kpi-label">Failed 24h</span>
      </div>
      <div className="kpi-card workers-kpi">
        <span className="kpi-value">
          <span className="healthy">{status.healthyWorkers}</span>
          <span className="separator">/</span>
          <span>{status.registeredWorkers}</span>
        </span>
        <span className="kpi-label">Workers Healthy</span>
      </div>
      {status.uptime && (
        <div className="kpi-card">
          <span className="kpi-value">{formatUptime(status.uptime)}</span>
          <span className="kpi-label">Uptime</span>
        </div>
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
  if (minutes > 0) parts.push(`${minutes}m`);
  
  return parts.length > 0 ? parts.join(' ') : '< 1m';
}
