// Monitoring API Types

export interface SystemStatus {
  timestamp: string;
  activeWorkflows: number;
  queuedWorkflows: number;
  waitingForSignalWorkflows: number;
  completedWorkflows24h: number;
  failedWorkflows24h: number;
  registeredWorkers: number;
  healthyWorkers: number;
  drainedWorkers: number;
  uptime: string;
  version?: string;
}

export type WorkerHealthState = 'Healthy' | 'Unhealthy' | 'Degraded' | 'Unknown';

export interface WorkerStatus {
  workerId: string;
  address: string;
  health: WorkerHealthState;
  lastHealthCheck: string;
  registeredAt: string;
  activeWorkflows: number;
  supportedWorkflows: string[];
  supportedAgents: string[];
  isDraining: boolean;
}

export interface WorkflowMonitoringSummary {
  runId: string;
  workflowName: string;
  status: string;
  createdAt: string;
  completedAt?: string;
  hasPendingSignal: boolean;
}

export interface WorkflowMetricsSnapshot {
  windowStart: string;
  windowEnd: string;
  workflowsStarted: number;
  workflowsCompleted: number;
  workflowsFailed: number;
  avgDurationMs: number;
  p50DurationMs: number;
  p95DurationMs: number;
  p99DurationMs: number;
  stepsExecuted: number;
  avgStepDurationMs: number;
  signalsProcessed: number;
}

export interface MonitoringEvent {
  eventId: string;
  eventType: string;
  timestamp: string;
  payload: unknown;
}

export interface WorkerEventPayload {
  workerId: string;
  address?: string;
  health?: string;
  previousHealth?: string;
  activeWorkflows?: number;
}

export interface WorkflowEventPayload {
  runId: string;
  workflowName?: string;
  status?: string;
  previousStatus?: string;
  stepName?: string;
  workerId?: string;
  errorMessage?: string;
  durationMs?: number;
}

// ============ Workflow Detail Types ============

export interface WorkflowMessage {
  typeName: string;
  data: unknown;
  metadata?: Record<string, string>;
}

export interface WorkflowStepInfo {
  stepId: string;
  executorId: string;
  executorName?: string;
  startedAt: string;
  completedAt?: string;
  output?: WorkflowMessage;
  durationMs?: number;
}

export interface PendingExternalRequest {
  requestId: string;
  portId: string;
  requestTypeName: string;
  responseTypeName: string;
  requestData: WorkflowMessage;
  title?: string;
  description?: string;
  uiHints?: Record<string, string>;
  requestedAt: string;
}

export interface WorkflowArtifact {
  id: string;
  name: string;
  contentType: string;
  content: string;
  createdAt: string;
  metadata?: Record<string, string>;
}

export interface WorkflowErrorInfo {
  code: string;
  message: string;
  stackTrace?: string;
}

export interface WorkflowRun {
  id: string;
  workflowName: string;
  status: string;
  input?: WorkflowMessage;
  createdAt: string;
  updatedAt?: string;
  completedAt?: string;
  steps: WorkflowStepInfo[];
  artifacts: WorkflowArtifact[];
  pendingRequests: PendingExternalRequest[];
  error?: WorkflowErrorInfo;
  metadata?: Record<string, string>;
  etag?: string;
}
