import type {
  WorkflowRun,
  WorkflowRunSummary,
  WorkflowListResponse,
  StartWorkflowRequest,
  ListWorkflowsRequest,
  WorkflowSignal,
  WorkflowMessage,
} from '../types';

const BASE_URL = '/v1/workflows';

class WorkflowApiClient {
  private async request<T>(
    url: string,
    options?: RequestInit
  ): Promise<T> {
    const response = await fetch(url, {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        ...options?.headers,
      },
    });

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`API Error ${response.status}: ${errorText}`);
    }

    return response.json();
  }

  async startWorkflow(request: StartWorkflowRequest): Promise<WorkflowRun> {
    return this.request<WorkflowRun>(BASE_URL, {
      method: 'POST',
      body: JSON.stringify(request),
    });
  }

  async getWorkflow(runId: string): Promise<WorkflowRun | null> {
    try {
      return await this.request<WorkflowRun>(
        `${BASE_URL}/${encodeURIComponent(runId)}`
      );
    } catch (error) {
      if (error instanceof Error && error.message.includes('404')) {
        return null;
      }
      throw error;
    }
  }

  async listWorkflows(
    request?: ListWorkflowsRequest
  ): Promise<WorkflowListResponse<WorkflowRunSummary>> {
    const params = new URLSearchParams();

    if (request?.status) {
      params.set('status', request.status);
    }
    if (request?.limit) {
      params.set('limit', request.limit.toString());
    }
    if (request?.after) {
      params.set('after', request.after);
    }
    if (request?.before) {
      params.set('before', request.before);
    }

    const queryString = params.toString();
    const url = queryString ? `${BASE_URL}?${queryString}` : BASE_URL;

    return this.request<WorkflowListResponse<WorkflowRunSummary>>(url);
  }

  async sendSignal(runId: string, signal: WorkflowSignal): Promise<WorkflowRun> {
    return this.request<WorkflowRun>(
      `${BASE_URL}/${encodeURIComponent(runId)}/signals`,
      {
        method: 'POST',
        body: JSON.stringify(signal),
      }
    );
  }

  async cancelWorkflow(runId: string): Promise<WorkflowRun> {
    return this.request<WorkflowRun>(
      `${BASE_URL}/${encodeURIComponent(runId)}/cancel`,
      {
        method: 'POST',
      }
    );
  }

  async abortWorkflow(runId: string, reason: string): Promise<WorkflowRun> {
    return this.request<WorkflowRun>(
      `${BASE_URL}/${encodeURIComponent(runId)}/abort`,
      {
        method: 'POST',
        body: JSON.stringify({ reason }),
      }
    );
  }
}

// Helper to create a WorkflowMessage
export function createWorkflowMessage<T>(
  data: T,
  metadata?: Record<string, string>
): WorkflowMessage {
  return {
    typeName: typeof data === 'object' ? (data as object).constructor.name : typeof data,
    data,
    metadata,
  };
}

// Singleton instance
export const workflowApi = new WorkflowApiClient();
