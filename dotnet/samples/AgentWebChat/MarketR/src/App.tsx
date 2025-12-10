import { useState, useEffect, useCallback } from 'react';
import { workflowApi } from './api';
import {
  StartWorkflowForm,
  WorkflowList,
  WorkflowDetails,
  Toast,
} from './components';
import type { WorkflowRun, WorkflowRunSummary } from './types';
import './App.css';

interface ToastState {
  message: string;
  type: 'success' | 'error' | 'info';
}

function App() {
  const [workflows, setWorkflows] = useState<WorkflowRunSummary[]>([]);
  const [selectedWorkflow, setSelectedWorkflow] = useState<WorkflowRun | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [toast, setToast] = useState<ToastState | null>(null);

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

export default App;
