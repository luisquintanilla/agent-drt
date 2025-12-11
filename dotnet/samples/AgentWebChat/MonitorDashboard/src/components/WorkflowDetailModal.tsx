import { useEffect, useState, useCallback } from 'react';
import type { WorkflowRun } from '../types';
import { monitoringApi } from '../api';
import './WorkflowDetailModal.css';

interface WorkflowDetailModalProps {
  runId: string;
  onClose: () => void;
  onWorkflowUpdated?: () => void;
}

type ConfirmAction = 'cancel' | 'abort' | 'delete' | null;

export function WorkflowDetailModal({ runId, onClose, onWorkflowUpdated }: WorkflowDetailModalProps) {
  const [workflow, setWorkflow] = useState<WorkflowRun | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [activeSection, setActiveSection] = useState<'overview' | 'steps' | 'artifacts' | 'requests'>('overview');
  const [confirmAction, setConfirmAction] = useState<ConfirmAction>(null);
  const [actionInProgress, setActionInProgress] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const fetchWorkflow = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await monitoringApi.getWorkflowDetails(runId);
      if (data) {
        setWorkflow(data);
      } else {
        setError(new Error('Workflow not found'));
      }
    } catch (e) {
      setError(e instanceof Error ? e : new Error(String(e)));
    } finally {
      setIsLoading(false);
    }
  }, [runId]);

  useEffect(() => {
    fetchWorkflow();
  }, [fetchWorkflow]);

  // Close on escape key (but not if confirm dialog is open)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        if (confirmAction) {
          setConfirmAction(null);
        } else {
          onClose();
        }
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose, confirmAction]);

  const handleCancelWorkflow = useCallback(async () => {
    setActionInProgress(true);
    setActionError(null);
    try {
      const updated = await monitoringApi.cancelWorkflow(runId);
      setWorkflow(updated);
      setConfirmAction(null);
      onWorkflowUpdated?.();
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e));
    } finally {
      setActionInProgress(false);
    }
  }, [runId, onWorkflowUpdated]);

  const handleAbortWorkflow = useCallback(async () => {
    setActionInProgress(true);
    setActionError(null);
    try {
      const updated = await monitoringApi.abortWorkflow(runId, 'Aborted via monitoring dashboard');
      setWorkflow(updated);
      setConfirmAction(null);
      onWorkflowUpdated?.();
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e));
    } finally {
      setActionInProgress(false);
    }
  }, [runId, onWorkflowUpdated]);

  const handleDeleteWorkflow = useCallback(async () => {
    setActionInProgress(true);
    setActionError(null);
    try {
      await monitoringApi.deleteWorkflow(runId);
      setConfirmAction(null);
      onWorkflowUpdated?.();
      onClose(); // Close the modal after successful deletion
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e));
    } finally {
      setActionInProgress(false);
    }
  }, [runId, onWorkflowUpdated, onClose]);

  // Check if workflow can be cancelled/aborted (only active workflows)
  const canCancel = workflow && !isTerminalStatus(workflow.status);
  const canAbort = workflow && !isTerminalStatus(workflow.status);
  // Can only delete workflows in terminal status
  const canDelete = workflow && isTerminalStatus(workflow.status);

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Workflow Details</h2>
          <button className="close-button" onClick={onClose} title="Close">
            X
          </button>
        </div>

        {isLoading && (
          <div className="modal-loading">
            <p>Loading workflow details...</p>
          </div>
        )}

        {error && (
          <div className="modal-error">
            <p>Error: {error.message}</p>
            <button onClick={fetchWorkflow} className="retry-button">Retry</button>
          </div>
        )}

        {workflow && (
          <>
            <div className="modal-tabs">
              <button
                className={`modal-tab ${activeSection === 'overview' ? 'active' : ''}`}
                onClick={() => setActiveSection('overview')}
              >
                Overview
              </button>
              <button
                className={`modal-tab ${activeSection === 'steps' ? 'active' : ''}`}
                onClick={() => setActiveSection('steps')}
              >
                Steps ({workflow.steps.length})
              </button>
              <button
                className={`modal-tab ${activeSection === 'artifacts' ? 'active' : ''}`}
                onClick={() => setActiveSection('artifacts')}
              >
                Artifacts ({workflow.artifacts.length})
              </button>
              <button
                className={`modal-tab ${activeSection === 'requests' ? 'active' : ''}`}
                onClick={() => setActiveSection('requests')}
              >
                Pending ({workflow.pendingRequests.length})
              </button>
            </div>

            <div className="modal-body">
              {activeSection === 'overview' && <OverviewSection workflow={workflow} />}
              {activeSection === 'steps' && <StepsSection workflow={workflow} />}
              {activeSection === 'artifacts' && <ArtifactsSection workflow={workflow} />}
              {activeSection === 'requests' && <PendingRequestsSection workflow={workflow} />}
            </div>

            <div className="modal-footer">
              <div className="footer-actions-left">
                {canCancel && (
                  <button
                    onClick={() => setConfirmAction('cancel')}
                    className="action-button cancel-workflow"
                    disabled={actionInProgress}
                  >
                    Cancel Workflow
                  </button>
                )}
                {canAbort && (
                  <button
                    onClick={() => setConfirmAction('abort')}
                    className="action-button abort-workflow"
                    disabled={actionInProgress}
                  >
                    Abort Workflow
                  </button>
                )}
                {canDelete && (
                  <button
                    onClick={() => setConfirmAction('delete')}
                    className="action-button delete-workflow"
                    disabled={actionInProgress}
                  >
                    Delete Run
                  </button>
                )}
              </div>
              <div className="footer-actions-right">
                <button onClick={fetchWorkflow} className="refresh-button">
                  Refresh
                </button>
                <button onClick={onClose} className="close-button-secondary">
                  Close
                </button>
              </div>
            </div>

            {/* Confirmation Dialog */}
            {confirmAction && (
              <ConfirmDialog
                action={confirmAction}
                workflowName={workflow.workflowName}
                runId={runId}
                isLoading={actionInProgress}
                error={actionError}
                onConfirm={
                  confirmAction === 'cancel'
                    ? handleCancelWorkflow
                    : confirmAction === 'abort'
                    ? handleAbortWorkflow
                    : handleDeleteWorkflow
                }
                onCancel={() => {
                  setConfirmAction(null);
                  setActionError(null);
                }}
              />
            )}
          </>
        )}
      </div>
    </div>
  );
}

// Confirmation dialog component
interface ConfirmDialogProps {
  action: 'cancel' | 'abort' | 'delete';
  workflowName: string;
  runId: string;
  isLoading: boolean;
  error: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

function ConfirmDialog({ action, workflowName, runId, isLoading, error, onConfirm, onCancel }: ConfirmDialogProps) {
  const isAbort = action === 'abort';
  const isDelete = action === 'delete';
  
  const getTitle = () => {
    if (isDelete) return 'Delete Run?';
    if (isAbort) return 'Abort Workflow?';
    return 'Cancel Workflow?';
  };

  const getMessage = () => {
    if (isDelete) {
      return (
        <>
          This will <strong>permanently delete</strong> this run and all its associated data.
          This action cannot be undone.
        </>
      );
    }
    if (isAbort) {
      return (
        <>
          This will <strong>forcefully stop</strong> the workflow immediately without cleanup.
          This action cannot be undone.
        </>
      );
    }
    return (
      <>
        This will request <strong>cooperative cancellation</strong> of the workflow.
        The workflow will be notified and given a chance to clean up.
      </>
    );
  };

  const getTitleClass = () => {
    if (isDelete) return 'delete-title';
    if (isAbort) return 'abort-title';
    return 'cancel-title';
  };

  const getConfirmButtonClass = () => {
    if (isDelete) return 'confirm-delete';
    if (isAbort) return 'confirm-abort';
    return 'confirm-cancel';
  };

  const getConfirmText = () => {
    if (isLoading) {
      if (isDelete) return 'Deleting...';
      if (isAbort) return 'Aborting...';
      return 'Cancelling...';
    }
    if (isDelete) return 'Yes, Delete';
    if (isAbort) return 'Yes, Abort';
    return 'Yes, Cancel';
  };

  const getDeclineText = () => {
    if (isDelete) return 'No, Keep It';
    return 'No, Keep Running';
  };
  
  return (
    <div className="confirm-overlay" onClick={onCancel}>
      <div className="confirm-dialog" onClick={(e) => e.stopPropagation()}>
        <h3 className={getTitleClass()}>
          {getTitle()}
        </h3>
        <p className="confirm-message">
          {getMessage()}
        </p>
        <div className="confirm-details">
          <div className="detail-row">
            <span className="label">Workflow:</span>
            <span className="value">{workflowName}</span>
          </div>
          <div className="detail-row">
            <span className="label">Run ID:</span>
            <span className="value monospace">{runId}</span>
          </div>
        </div>
        {error && (
          <div className="confirm-error">
            Error: {error}
          </div>
        )}
        <div className="confirm-actions">
          <button
            onClick={onCancel}
            className="confirm-button confirm-no"
            disabled={isLoading}
          >
            {getDeclineText()}
          </button>
          <button
            onClick={onConfirm}
            className={`confirm-button ${getConfirmButtonClass()}`}
            disabled={isLoading}
          >
            {getConfirmText()}
          </button>
        </div>
      </div>
    </div>
  );
}

// Helper function to check if status is terminal
function isTerminalStatus(status: string): boolean {
  const lower = status.toLowerCase();
  return (
    lower.includes('completed') ||
    lower.includes('failed') ||
    lower.includes('cancelled') ||
    lower.includes('canceled') ||
    lower.includes('aborted')
  );
}

function OverviewSection({ workflow }: { workflow: WorkflowRun }) {
  return (
    <div className="section overview-section">
      <div className="detail-grid">
        <div className="detail-item">
          <span className="label">Run ID</span>
          <span className="value monospace">{workflow.id}</span>
        </div>
        <div className="detail-item">
          <span className="label">Workflow Name</span>
          <span className="value">{workflow.workflowName}</span>
        </div>
        <div className="detail-item">
          <span className="label">Status</span>
          <span className={`status-badge ${getStatusClass(workflow.status)}`}>
            {workflow.status}
          </span>
        </div>
        <div className="detail-item">
          <span className="label">Created At</span>
          <span className="value">{formatDateTime(workflow.createdAt)}</span>
        </div>
        {workflow.updatedAt && (
          <div className="detail-item">
            <span className="label">Updated At</span>
            <span className="value">{formatDateTime(workflow.updatedAt)}</span>
          </div>
        )}
        {workflow.completedAt && (
          <div className="detail-item">
            <span className="label">Completed At</span>
            <span className="value">{formatDateTime(workflow.completedAt)}</span>
          </div>
        )}
        {workflow.completedAt && workflow.createdAt && (
          <div className="detail-item">
            <span className="label">Duration</span>
            <span className="value">{formatDuration(workflow.createdAt, workflow.completedAt)}</span>
          </div>
        )}
        {workflow.etag && (
          <div className="detail-item">
            <span className="label">ETag</span>
            <span className="value monospace">{workflow.etag}</span>
          </div>
        )}
      </div>

      {workflow.error && (
        <div className="error-section">
          <h3>Error</h3>
          <div className="error-details">
            <div className="detail-item">
              <span className="label">Code</span>
              <span className="value error-code">{workflow.error.code}</span>
            </div>
            <div className="detail-item full-width">
              <span className="label">Message</span>
              <span className="value">{workflow.error.message}</span>
            </div>
            {workflow.error.stackTrace && (
              <div className="detail-item full-width">
                <span className="label">Stack Trace</span>
                <pre className="stack-trace">{workflow.error.stackTrace}</pre>
              </div>
            )}
          </div>
        </div>
      )}

      {workflow.input && (
        <div className="input-section">
          <h3>Input</h3>
          <div className="detail-item">
            <span className="label">Type</span>
            <span className="value monospace">{workflow.input.typeName}</span>
          </div>
          <div className="json-viewer">
            <pre>{JSON.stringify(workflow.input.data, null, 2)}</pre>
          </div>
        </div>
      )}

      {workflow.metadata && Object.keys(workflow.metadata).length > 0 && (
        <div className="metadata-section">
          <h3>Metadata</h3>
          <div className="metadata-grid">
            {Object.entries(workflow.metadata).map(([key, value]) => (
              <div key={key} className="detail-item">
                <span className="label">{key}</span>
                <span className="value">{value}</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function StepsSection({ workflow }: { workflow: WorkflowRun }) {
  const [expandedStep, setExpandedStep] = useState<string | null>(null);

  if (workflow.steps.length === 0) {
    return (
      <div className="section empty-section">
        <p>No steps recorded yet.</p>
      </div>
    );
  }

  return (
    <div className="section steps-section">
      <div className="steps-list">
        {workflow.steps.map((step, index) => (
          <div key={step.stepId} className={`step-item ${step.completedAt ? 'completed' : 'in-progress'}`}>
            <div
              className="step-header"
              onClick={() => setExpandedStep(expandedStep === step.stepId ? null : step.stepId)}
            >
              <span className="step-index">{index + 1}</span>
              <div className="step-info">
                <span className="step-name">{step.executorName || step.executorId}</span>
                <span className="step-id">ID: {step.stepId}</span>
              </div>
              <div className="step-timing">
                {step.completedAt ? (
                  <span className="duration">{step.durationMs}ms</span>
                ) : (
                  <span className="in-progress-badge">In Progress</span>
                )}
              </div>
              <span className="expand-icon">{expandedStep === step.stepId ? '-' : '+'}</span>
            </div>

            {expandedStep === step.stepId && (
              <div className="step-details">
                <div className="detail-grid">
                  <div className="detail-item">
                    <span className="label">Executor ID</span>
                    <span className="value monospace">{step.executorId}</span>
                  </div>
                  <div className="detail-item">
                    <span className="label">Started At</span>
                    <span className="value">{formatDateTime(step.startedAt)}</span>
                  </div>
                  {step.completedAt && (
                    <div className="detail-item">
                      <span className="label">Completed At</span>
                      <span className="value">{formatDateTime(step.completedAt)}</span>
                    </div>
                  )}
                </div>
                {step.output && (
                  <div className="step-output">
                    <h4>Output</h4>
                    <div className="detail-item">
                      <span className="label">Type</span>
                      <span className="value monospace">{step.output.typeName}</span>
                    </div>
                    <div className="json-viewer">
                      <pre>{JSON.stringify(step.output.data, null, 2)}</pre>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function ArtifactsSection({ workflow }: { workflow: WorkflowRun }) {
  const [expandedArtifact, setExpandedArtifact] = useState<string | null>(null);

  if (workflow.artifacts.length === 0) {
    return (
      <div className="section empty-section">
        <p>No artifacts generated.</p>
      </div>
    );
  }

  return (
    <div className="section artifacts-section">
      <div className="artifacts-list">
        {workflow.artifacts.map((artifact) => (
          <div key={artifact.id} className="artifact-item">
            <div
              className="artifact-header"
              onClick={() => setExpandedArtifact(expandedArtifact === artifact.id ? null : artifact.id)}
            >
              <div className="artifact-info">
                <span className="artifact-name">{artifact.name}</span>
                <span className="artifact-type">{artifact.contentType}</span>
              </div>
              <span className="artifact-time">{formatDateTime(artifact.createdAt)}</span>
              <span className="expand-icon">{expandedArtifact === artifact.id ? '-' : '+'}</span>
            </div>

            {expandedArtifact === artifact.id && (
              <div className="artifact-details">
                <div className="detail-item">
                  <span className="label">ID</span>
                  <span className="value monospace">{artifact.id}</span>
                </div>
                <div className="artifact-content">
                  <h4>Content</h4>
                  <pre>{artifact.content}</pre>
                </div>
                {artifact.metadata && Object.keys(artifact.metadata).length > 0 && (
                  <div className="artifact-metadata">
                    <h4>Metadata</h4>
                    <div className="metadata-grid">
                      {Object.entries(artifact.metadata).map(([key, value]) => (
                        <div key={key} className="detail-item">
                          <span className="label">{key}</span>
                          <span className="value">{value}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function PendingRequestsSection({ workflow }: { workflow: WorkflowRun }) {
  const [expandedRequest, setExpandedRequest] = useState<string | null>(null);

  if (workflow.pendingRequests.length === 0) {
    return (
      <div className="section empty-section">
        <p>No pending requests.</p>
      </div>
    );
  }

  return (
    <div className="section requests-section">
      <div className="requests-list">
        {workflow.pendingRequests.map((request) => (
          <div key={request.requestId} className="request-item">
            <div
              className="request-header"
              onClick={() => setExpandedRequest(expandedRequest === request.requestId ? null : request.requestId)}
            >
              <div className="request-info">
                <span className="request-title">{request.title || request.portId}</span>
                {request.description && (
                  <span className="request-description">{request.description}</span>
                )}
              </div>
              <span className="request-time">{formatDateTime(request.requestedAt)}</span>
              <span className="expand-icon">{expandedRequest === request.requestId ? '-' : '+'}</span>
            </div>

            {expandedRequest === request.requestId && (
              <div className="request-details">
                <div className="detail-grid">
                  <div className="detail-item">
                    <span className="label">Request ID</span>
                    <span className="value monospace">{request.requestId}</span>
                  </div>
                  <div className="detail-item">
                    <span className="label">Port ID</span>
                    <span className="value monospace">{request.portId}</span>
                  </div>
                  <div className="detail-item">
                    <span className="label">Request Type</span>
                    <span className="value monospace">{request.requestTypeName}</span>
                  </div>
                  <div className="detail-item">
                    <span className="label">Response Type</span>
                    <span className="value monospace">{request.responseTypeName}</span>
                  </div>
                </div>
                <div className="request-data">
                  <h4>Request Data</h4>
                  <div className="json-viewer">
                    <pre>{JSON.stringify(request.requestData.data, null, 2)}</pre>
                  </div>
                </div>
                {request.uiHints && Object.keys(request.uiHints).length > 0 && (
                  <div className="request-hints">
                    <h4>UI Hints</h4>
                    <div className="metadata-grid">
                      {Object.entries(request.uiHints).map(([key, value]) => (
                        <div key={key} className="detail-item">
                          <span className="label">{key}</span>
                          <span className="value">{value}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

// Helper functions
function getStatusClass(status: string): string {
  const lower = status.toLowerCase();
  if (lower.includes('running') || lower.includes('executing')) return 'running';
  if (lower.includes('completed') || lower.includes('success')) return 'completed';
  if (lower.includes('failed') || lower.includes('error')) return 'failed';
  if (lower.includes('waiting') || lower.includes('pending') || lower.includes('queued')) return 'waiting';
  if (lower.includes('cancelled') || lower.includes('canceled') || lower.includes('aborted')) return 'cancelled';
  return 'unknown';
}

function formatDateTime(isoDate: string): string {
  const date = new Date(isoDate);
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

function formatDuration(startIso: string, endIso: string): string {
  const start = new Date(startIso);
  const end = new Date(endIso);
  const diffMs = end.getTime() - start.getTime();
  
  if (diffMs < 1000) return `${diffMs}ms`;
  if (diffMs < 60000) return `${(diffMs / 1000).toFixed(1)}s`;
  if (diffMs < 3600000) return `${Math.floor(diffMs / 60000)}m ${Math.floor((diffMs % 60000) / 1000)}s`;
  return `${Math.floor(diffMs / 3600000)}h ${Math.floor((diffMs % 3600000) / 60000)}m`;
}
