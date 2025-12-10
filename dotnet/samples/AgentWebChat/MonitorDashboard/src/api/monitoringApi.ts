import type {
  SystemStatus,
  WorkerStatus,
  WorkflowMonitoringSummary,
  WorkflowMetricsSnapshot,
  WorkflowRun,
} from '../types';

const BASE_URL = '/v1/monitor';
const WORKFLOWS_URL = '/v1/workflows';

class MonitoringApiClient {
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

  /**
   * Get overall system status including workflow and worker counts.
   */
  async getSystemStatus(): Promise<SystemStatus> {
    return this.request<SystemStatus>(`${BASE_URL}/status`);
  }

  /**
   * Get list of all registered workers with their health status.
   */
  async getWorkers(): Promise<WorkerStatus[]> {
    return this.request<WorkerStatus[]>(`${BASE_URL}/workers`);
  }

  /**
   * Get details for a specific worker.
   */
  async getWorker(workerId: string): Promise<WorkerStatus | null> {
    try {
      return await this.request<WorkerStatus>(
        `${BASE_URL}/workers/${encodeURIComponent(workerId)}`
      );
    } catch (error) {
      if (error instanceof Error && error.message.includes('404')) {
        return null;
      }
      throw error;
    }
  }

  /**
   * Get list of currently active workflows.
   */
  async getActiveWorkflows(): Promise<WorkflowMonitoringSummary[]> {
    return this.request<WorkflowMonitoringSummary[]>(`${BASE_URL}/workflows/active`);
  }

  /**
   * Get list of recent workflows (both active and completed).
   */
  async getRecentWorkflows(count?: number): Promise<WorkflowMonitoringSummary[]> {
    const params = new URLSearchParams();
    if (count) {
      params.set('count', count.toString());
    }
    const queryString = params.toString();
    const url = queryString 
      ? `${BASE_URL}/workflows/recent?${queryString}` 
      : `${BASE_URL}/workflows/recent`;
    return this.request<WorkflowMonitoringSummary[]>(url);
  }

  /**
   * Get workflow metrics for a time window.
   */
  async getMetrics(windowMinutes?: number): Promise<WorkflowMetricsSnapshot> {
    const params = new URLSearchParams();
    if (windowMinutes) {
      params.set('windowMinutes', windowMinutes.toString());
    }
    const queryString = params.toString();
    const url = queryString 
      ? `${BASE_URL}/metrics?${queryString}` 
      : `${BASE_URL}/metrics`;
    return this.request<WorkflowMetricsSnapshot>(url);
  }

  /**
   * Request a worker to start draining (stop accepting new work).
   */
  async drainWorker(workerId: string): Promise<void> {
    await this.request<void>(
      `${BASE_URL}/workers/${encodeURIComponent(workerId)}/drain`,
      { method: 'POST' }
    );
  }

  /**
   * Re-enable a drained worker.
   */
  async enableWorker(workerId: string): Promise<void> {
    await this.request<void>(
      `${BASE_URL}/workers/${encodeURIComponent(workerId)}/enable`,
      { method: 'POST' }
    );
  }

  /**
   * Get detailed information about a specific workflow run.
   */
  async getWorkflowDetails(runId: string): Promise<WorkflowRun | null> {
    try {
      return await this.request<WorkflowRun>(
        `${WORKFLOWS_URL}/${encodeURIComponent(runId)}`
      );
    } catch (error) {
      if (error instanceof Error && error.message.includes('404')) {
        return null;
      }
      throw error;
    }
  }
}

// Singleton instance
export const monitoringApi = new MonitoringApiClient();
