import type { WorkflowRunStatus } from '../types';
import './StatusBadge.css';

interface StatusBadgeProps {
  status: WorkflowRunStatus;
  size?: 'small' | 'large';
}

const statusLabels: Record<WorkflowRunStatus, string> = {
  Queued: 'Queued',
  Running: 'Running',
  WaitingForSignal: 'Waiting',
  Cancelling: 'Cancelling',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
  Aborted: 'Aborted',
  Failed: 'Failed',
};

export function StatusBadge({ status, size = 'small' }: StatusBadgeProps) {
  const statusClass = status.toLowerCase().replace('forsignal', '');
  
  return (
    <span className={`status-badge status-${statusClass} size-${size}`}>
      {statusLabels[status]}
    </span>
  );
}
