import type { WorkerStatus, MonitoringEvent } from '../types';
import { WorkerListWidget } from '../components';
import { EventFeedWidget } from '../components';
import './WorkersPage.css';

interface WorkersPageProps {
  workers: WorkerStatus[];
  stats: {
    registered: number;
    healthy: number;
    drained: number;
  } | null;
  isLoading: boolean;
  error: Error | null;
  onRefresh: () => void;
  events: MonitoringEvent[];
  isConnected: boolean;
  connectionError: Error | null;
  onReconnect: () => void;
}

export function WorkersPage({
  workers,
  stats,
  isLoading,
  error,
  onRefresh,
  events,
  isConnected,
  connectionError,
  onReconnect,
}: WorkersPageProps) {
  // Filter to worker events only
  const workerEvents = events.filter(e => e.eventType.toLowerCase().includes('worker'));

  return (
    <div className="workers-page">
      <header className="page-header">
        <h1>Workers</h1>
        <p className="page-description">Manage and monitor worker instances</p>
      </header>

      <div className="workers-page-content">
        <div className="workers-main">
          <WorkerListWidget
            workers={workers}
            stats={stats}
            isLoading={isLoading}
            error={error}
            onRefresh={onRefresh}
          />
        </div>

        <div className="workers-events">
          <EventFeedWidget
            events={workerEvents}
            maxEvents={50}
            isConnected={isConnected}
            connectionError={connectionError}
            onReconnect={onReconnect}
          />
        </div>
      </div>
    </div>
  );
}
