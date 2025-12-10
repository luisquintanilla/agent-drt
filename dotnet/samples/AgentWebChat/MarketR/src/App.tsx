import { useState, useEffect, useCallback, useRef } from 'react';
import { workflowApi } from './api';
import {
  StartWorkflowForm,
  WorkflowList,
  WorkflowDetails,
  Toast,
} from './components';
import { useWorkflowEvents } from './hooks';
import type {
  WorkflowRun,
  WorkflowRunSummary,
  WorkflowRunStatus,
  WorkflowStatusEvent,
  WorkflowStepInfo,
} from './types';
import './App.css';

interface ToastState {
  message: string;
  type: 'success' | 'error' | 'info';
}

/** Terminal workflow statuses that don't need SSE streaming */
const TERMINAL_STATUSES: WorkflowRunStatus[] = [
  'Completed',
  'Cancelled',
  'Aborted',
  'Failed',
];

function App() {
  const [workflows, setWorkflows] = useState<WorkflowRunSummary[]>([]);
  const [selectedWorkflow, setSelectedWorkflow] = useState<WorkflowRun | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [toast, setToast] = useState<ToastState | null>(null);
  const [sseEnabled, setSseEnabled] = useState(true);

  // Track the selected workflow ID for SSE subscription
  const selectedIdRef = useRef<string | null>(null);
  selectedIdRef.current = selectedWorkflow?.id ?? null;

  // Determine if SSE should be active for the selected workflow
  const shouldSubscribeToSSE =
    sseEnabled &&
    selectedWorkflow != null &&
    !TERMINAL_STATUSES.includes(selectedWorkflow.status);

  // Handle SSE events to update UI in real-time
  const handleWorkflowEvent = useCallback((event: WorkflowStatusEvent) => {
    // Ensure the event is for the currently selected workflow
    if (event.runId !== selectedIdRef.current) {
      return;
    }

    setSelectedWorkflow((prev) => {
      if (!prev || prev.id !== event.runId) {
        return prev;
      }

      switch (event.type) {
        case 'workflow.started':
          return {
            ...prev,
            status: 'Running',
            updatedAt: event.timestamp,
          };

        case 'step.started': {
          const newStep: WorkflowStepInfo = {
            stepId: event.step.stepId,
            executorId: event.step.executorId,
            executorName: event.step.executorName,
            startedAt: event.step.startedAt,
          };
          // Add step if not already present
          const stepExists = prev.steps.some((s) => s.stepId === newStep.stepId);
          return {
            ...prev,
            status: 'Running',
            steps: stepExists ? prev.steps : [...prev.steps, newStep],
            updatedAt: event.timestamp,
          };
        }

        case 'step.completed': {
          return {
            ...prev,
            steps: prev.steps.map((s) =>
              s.stepId === event.step.stepId
                ? {
                    ...s,
                    completedAt: event.step.completedAt,
                    output: event.step.output,
                    durationMs: event.step.durationMs,
                  }
                : s
            ),
            updatedAt: event.timestamp,
          };
        }

        case 'signal.requested':
          return {
            ...prev,
            status: 'WaitingForSignal',
            pendingRequests: [...prev.pendingRequests, event.request],
            updatedAt: event.timestamp,
          };

        case 'signal.received':
          return {
            ...prev,
            status: 'Running',
            pendingRequests: prev.pendingRequests.filter(
              (r) => r.requestId !== event.requestId
            ),
            updatedAt: event.timestamp,
          };

        case 'artifact.created':
          return {
            ...prev,
            artifacts: [...prev.artifacts, event.artifact],
            updatedAt: event.timestamp,
          };

        case 'workflow.completed':
          // Full workflow state provided
          return event.workflow;

        case 'workflow.completed.signal':
          return {
            ...prev,
            status: 'Completed',
            completedAt: event.timestamp,
            updatedAt: event.timestamp,
          };

        case 'workflow.failed':
          return {
            ...prev,
            status: 'Failed',
            error: event.error,
            completedAt: event.timestamp,
            updatedAt: event.timestamp,
          };

        case 'workflow.cancelled':
          return {
            ...prev,
            status: 'Cancelled',
            completedAt: event.timestamp,
            updatedAt: event.timestamp,
          };

        case 'workflow.aborted':
          return {
            ...prev,
            status: 'Aborted',
            completedAt: event.timestamp,
            updatedAt: event.timestamp,
          };

        default:
          return prev;
      }
    });

    // Also update the workflow list summary for status changes
    const statusUpdateEvents = [
      'workflow.started',
      'workflow.completed',
      'workflow.completed.signal',
      'workflow.failed',
      'workflow.cancelled',
      'workflow.aborted',
      'signal.requested',
      'signal.received',
    ];

    if (statusUpdateEvents.includes(event.type)) {
      setWorkflows((prevList) =>
        prevList.map((w) => {
          if (w.id !== event.runId) {
            return w;
          }

          let newStatus: WorkflowRunStatus = w.status;
          let pendingRequestCount = w.pendingRequestCount;
          let completedAt = w.completedAt;

          switch (event.type) {
            case 'workflow.started':
              newStatus = 'Running';
              break;
            case 'workflow.completed':
            case 'workflow.completed.signal':
              newStatus = 'Completed';
              completedAt = event.timestamp;
              break;
            case 'workflow.failed':
              newStatus = 'Failed';
              completedAt = event.timestamp;
              break;
            case 'workflow.cancelled':
              newStatus = 'Cancelled';
              completedAt = event.timestamp;
              break;
            case 'workflow.aborted':
              newStatus = 'Aborted';
              completedAt = event.timestamp;
              break;
            case 'signal.requested':
              newStatus = 'WaitingForSignal';
              pendingRequestCount += 1;
              break;
            case 'signal.received':
              newStatus = 'Running';
              pendingRequestCount = Math.max(0, pendingRequestCount - 1);
              break;
          }

          return {
            ...w,
            status: newStatus,
            pendingRequestCount,
            completedAt,
          };
        })
      );
    }
  }, []);

  // Subscribe to SSE events for the selected workflow
  const { isConnected, error: sseError, reconnect } = useWorkflowEvents(
    shouldSubscribeToSSE ? selectedWorkflow?.id : null,
    {
      onEvent: handleWorkflowEvent,
      enabled: shouldSubscribeToSSE,
    }
  );

  const loadWorkflows = useCallback(async () => {
    setIsLoading(true);
    try {
      const response = await workflowApi.listWorkflows({ limit: 50 });
      setWorkflows(response.data);
    } catch (error) {
      showError(error instanceof Error ? error.message : 'Failed to load workflows');
    } finally {
      setIsLoading(false);
    }
  }, []);

  const loadWorkflowDetails = useCallback(async (id: string) => {
    try {
      const workflow = await workflowApi.getWorkflow(id);
      setSelectedWorkflow(workflow);
    } catch (error) {
      showError(error instanceof Error ? error.message : 'Failed to load workflow details');
    }
  }, []);

  useEffect(() => {
    loadWorkflows();
  }, [loadWorkflows]);

  // Log SSE connection status changes
  useEffect(() => {
    if (sseError) {
      console.warn('SSE connection error:', sseError.message);
    }
  }, [sseError]);

  const handleWorkflowSelect = async (id: string) => {
    await loadWorkflowDetails(id);
  };

  const handleWorkflowStarted = async () => {
    showSuccess('Workflow started successfully!');
    await loadWorkflows();
  };

  const handleWorkflowUpdate = async () => {
    await loadWorkflows();
    if (selectedWorkflow) {
      await loadWorkflowDetails(selectedWorkflow.id);
    }
  };

  const showError = (message: string) => {
    setToast({ message, type: 'error' });
    setTimeout(() => setToast(null), 5000);
  };

  const showSuccess = (message: string) => {
    setToast({ message, type: 'success' });
    setTimeout(() => setToast(null), 5000);
  };

  return (
    <div className="app">
      <header className="app-header">
        <nav className="nav-container">
          <div className="nav-brand">
            <WorkflowIcon />
            <span>MarketR</span>
          </div>
          <p className="nav-subtitle">Marketing Content Workflows</p>
          {/* SSE Connection Status Indicator */}
          <div className="sse-status">
            <button
              className={`sse-toggle ${sseEnabled ? 'enabled' : 'disabled'}`}
              onClick={() => setSseEnabled((prev) => !prev)}
              title={sseEnabled ? 'Live updates enabled' : 'Live updates disabled'}
            >
              <LiveIcon />
              <span>{sseEnabled ? 'Live' : 'Paused'}</span>
            </button>
            {shouldSubscribeToSSE && (
              <span
                className={`connection-indicator ${isConnected ? 'connected' : 'disconnected'}`}
                title={
                  isConnected
                    ? 'Connected to event stream'
                    : sseError
                    ? `Connection error: ${sseError.message}`
                    : 'Connecting...'
                }
                onClick={!isConnected ? reconnect : undefined}
              >
                {isConnected ? <SignalIcon /> : <SignalOffIcon />}
              </span>
            )}
          </div>
        </nav>
      </header>

      <main className="app-main">
        <div className="container">
          <div className="page-header">
            <h1>
              <CheckCircleIcon />
              Human-in-the-Loop Workflows
            </h1>
            <p>Create and manage marketing content workflows with human approval</p>
          </div>

          <StartWorkflowForm
            onWorkflowStarted={handleWorkflowStarted}
            onError={showError}
          />

          <WorkflowList
            workflows={workflows}
            selectedId={selectedWorkflow?.id}
            isLoading={isLoading}
            onSelect={handleWorkflowSelect}
            onRefresh={loadWorkflows}
          />

          {selectedWorkflow && (
            <WorkflowDetails
              workflow={selectedWorkflow}
              onUpdate={handleWorkflowUpdate}
              onClose={() => setSelectedWorkflow(null)}
              onError={showError}
              onSuccess={showSuccess}
            />
          )}
        </div>
      </main>

      {toast && (
        <Toast
          message={toast.message}
          type={toast.type}
          onClose={() => setToast(null)}
        />
      )}
    </div>
  );
}

function WorkflowIcon() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
      <polyline points="22 4 12 14.01 9 11.01" />
    </svg>
  );
}

function CheckCircleIcon() {
  return (
    <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
      <polyline points="22 4 12 14.01 9 11.01" />
    </svg>
  );
}

function LiveIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
      <circle cx="12" cy="12" r="6" />
    </svg>
  );
}

function SignalIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M5 12.55a11 11 0 0 1 14.08 0" />
      <path d="M1.42 9a16 16 0 0 1 21.16 0" />
      <path d="M8.53 16.11a6 6 0 0 1 6.95 0" />
      <circle cx="12" cy="20" r="1" />
    </svg>
  );
}

function SignalOffIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="1" y1="1" x2="23" y2="23" />
      <path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55" />
      <path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39" />
      <path d="M10.71 5.05A16 16 0 0 1 22.58 9" />
      <path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88" />
      <path d="M8.53 16.11a6 6 0 0 1 6.95 0" />
      <circle cx="12" cy="20" r="1" />
    </svg>
  );
}

export default App;
