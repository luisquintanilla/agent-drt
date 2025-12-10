// Workflow types matching the C# models in AgentContracts.Workflows

export type WorkflowRunStatus = 
  | 'Queued'
  | 'Running'
  | 'WaitingForSignal'
  | 'Cancelling'
  | 'Completed'
  | 'Cancelled'
  | 'Aborted'
  | 'Failed';

export interface WorkflowMessage {
  typeName: string;
  data: unknown;
  metadata?: Record<string, string>;
}

export interface WorkflowSignal {
  requestId: string;
  response: WorkflowMessage;
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

export interface WorkflowArtifactRecord {
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
  status: WorkflowRunStatus;
  input?: WorkflowMessage;
  createdAt: string;
  updatedAt?: string;
  completedAt?: string;
  steps: WorkflowStepInfo[];
  artifacts: WorkflowArtifactRecord[];
  pendingRequests: PendingExternalRequest[];
  error?: WorkflowErrorInfo;
  metadata?: Record<string, string>;
  etag?: string;
}

export interface WorkflowRunSummary {
  id: string;
  workflowName: string;
  status: WorkflowRunStatus;
  createdAt: string;
  completedAt?: string;
  pendingRequestCount: number;
  etag?: string;
}

export interface StartWorkflowRequest {
  workflowName: string;
  input: WorkflowMessage;
  metadata?: Record<string, string>;
  options?: Record<string, string>;
}

export interface ListWorkflowsRequest {
  status?: WorkflowRunStatus;
  limit?: number;
  after?: string;
  before?: string;
}

export interface WorkflowListResponse<T> {
  object: 'list';
  data: T[];
  firstId?: string;
  lastId?: string;
  hasMore: boolean;
}

// Marketing workflow specific types
export interface ContentRequest {
  topic?: string;
  targetAudience?: string;
  tone?: string;
}

export interface ApprovalRequest {
  content?: string;
  options?: string[];
}

export interface ApprovalResponse {
  decision?: string;
  feedback?: string;
}
