import { useState } from 'react';
import type { WorkflowRun, ApprovalRequest } from '../types';
import { workflowApi, createWorkflowMessage } from '../api';
import { StatusBadge } from './StatusBadge';
import './WorkflowDetails.css';

interface WorkflowDetailsProps {
  workflow: WorkflowRun;
  onUpdate: () => void;
  onClose: () => void;
  onError: (message: string) => void;
  onSuccess: (message: string) => void;
}

export function WorkflowDetails({
  workflow,
  onUpdate,
  onClose,
  onError,
  onSuccess,
}: WorkflowDetailsProps) {
  const [feedback, setFeedback] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submittingAction, setSubmittingAction] = useState<string | null>(null);
  const [isCancelling, setIsCancelling] = useState(false);

  const handleApproval = async (requestId: string, decision: string) => {
    setIsSubmitting(true);
    setSubmittingAction(decision);

    try {
      await workflowApi.sendSignal(workflow.id, {
        requestId,
        response: createWorkflowMessage({
          decision,
          feedback: feedback || undefined,
        }),
      });

      setFeedback('');
      onSuccess(`Decision '${decision}' submitted successfully!`);
      onUpdate();
    } catch (error) {
      onError(error instanceof Error ? error.message : 'Failed to submit approval');
    } finally {
      setIsSubmitting(false);
      setSubmittingAction(null);
    }
  };

  const handleCancel = async () => {
    setIsCancelling(true);

    try {
      await workflowApi.cancelWorkflow(workflow.id);
      onSuccess('Workflow cancelled');
      onUpdate();
    } catch (error) {
      onError(error instanceof Error ? error.message : 'Failed to cancel workflow');
    } finally {
      setIsCancelling(false);
    }
  };

  const canCancel =
    workflow.status === 'Running' || workflow.status === 'WaitingForSignal';

  return (
    <div className="workflow-details">
      <div className="details-header">
        <h2>Workflow Details</h2>
        <StatusBadge status={workflow.status} size="large" />
      </div>

      {/* Information Section */}
      <section className="details-section">
        <h3>Information</h3>
        <div className="details-grid">
          <div className="detail-item">
            <label>Workflow ID</label>
            <span className="monospace">{workflow.id}</span>
          </div>
          <div className="detail-item">
            <label>Workflow Name</label>
            <span>{workflow.workflowName}</span>
          </div>
          <div className="detail-item">
            <label>Created</label>
            <span>{new Date(workflow.createdAt).toLocaleString()}</span>
          </div>
          {workflow.completedAt && (
            <div className="detail-item">
              <label>Completed</label>
              <span>{new Date(workflow.completedAt).toLocaleString()}</span>
            </div>
          )}
        </div>
      </section>

      {/* Steps Timeline */}
      {workflow.steps.length > 0 && (
        <section className="details-section">
          <h3>Steps</h3>
          <div className="steps-timeline">
            {workflow.steps.map((step) => (
              <div
                key={step.stepId}
                className={`step-item ${step.completedAt ? 'completed' : 'in-progress'}`}
              >
                <div className="step-icon">
                  {step.completedAt ? <CheckIcon /> : <span className="spinner" />}
                </div>
                <div className="step-content">
                  <div className="step-name">{step.executorName || step.executorId}</div>
                  <div className="step-meta">
                    <span>Started: {new Date(step.startedAt).toLocaleTimeString()}</span>
                    {step.durationMs && <span>Duration: {step.durationMs}ms</span>}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </section>
      )}

      {/* Pending Approvals */}
      {workflow.pendingRequests.length > 0 && (
        <section className="details-section approval-section">
          <h3>Pending Approvals</h3>
          {workflow.pendingRequests.map((request) => {
            const approvalData = request.requestData?.data as ApprovalRequest | undefined;

            return (
              <div key={request.requestId} className="approval-card">
                <div className="approval-header">
                  <h4>{request.title || 'Approval Required'}</h4>
                  <span className="request-id">
                    Request: {request.requestId.substring(0, 8)}...
                  </span>
                </div>

                {request.description && (
                  <p className="approval-description">{request.description}</p>
                )}

                {approvalData?.content && (
                  <div className="content-preview">
                    <label>Content to Review:</label>
                    <pre className="content-text">{approvalData.content}</pre>
                  </div>
                )}

                <div className="approval-actions">
                  <div className="feedback-input">
                    <label htmlFor={`feedback-${request.requestId}`}>
                      Feedback (optional):
                    </label>
                    <textarea
                      id={`feedback-${request.requestId}`}
                      value={feedback}
                      onChange={(e) => setFeedback(e.target.value)}
                      placeholder="Add any feedback or notes..."
                      disabled={isSubmitting}
                    />
                  </div>

                  <div className="action-buttons">
                    <button
                      className="btn btn-approve"
                      onClick={() => handleApproval(request.requestId, 'approve')}
                      disabled={isSubmitting}
                    >
                      {isSubmitting && submittingAction === 'approve' ? (
                        <span className="spinner" />
                      ) : (
                        <CheckIcon />
                      )}
                      Approve
                    </button>
                    <button
                      className="btn btn-revise"
                      onClick={() => handleApproval(request.requestId, 'revise')}
                      disabled={isSubmitting}
                    >
                      {isSubmitting && submittingAction === 'revise' ? (
                        <span className="spinner" />
                      ) : (
                        <EditIcon />
                      )}
                      Request Revision
                    </button>
                    <button
                      className="btn btn-reject"
                      onClick={() => handleApproval(request.requestId, 'reject')}
                      disabled={isSubmitting}
                    >
                      {isSubmitting && submittingAction === 'reject' ? (
                        <span className="spinner" />
                      ) : (
                        <XIcon />
                      )}
                      Reject
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </section>
      )}

      {/* Artifacts */}
      {workflow.artifacts.length > 0 && (
        <section className="details-section">
          <h3>Artifacts</h3>
          {workflow.artifacts.map((artifact) => (
            <div key={artifact.id} className="artifact-card">
              <div className="artifact-header">
                <h4>{artifact.name}</h4>
                <span className="artifact-type">{artifact.contentType}</span>
              </div>
              <pre className="artifact-content">{artifact.content}</pre>
              {artifact.metadata && Object.keys(artifact.metadata).length > 0 && (
                <div className="artifact-metadata">
                  {Object.entries(artifact.metadata).map(([key, value]) => (
                    <span key={key} className="metadata-tag">
                      {key}: {value}
                    </span>
                  ))}
                </div>
              )}
            </div>
          ))}
        </section>
      )}

      {/* Error Display */}
      {workflow.error && (
        <section className="details-section error-section">
          <h3>Error</h3>
          <div className="error-display">
            <div className="error-code">{workflow.error.code}</div>
            <div className="error-message">{workflow.error.message}</div>
            {workflow.error.stackTrace && (
              <pre className="error-stack">{workflow.error.stackTrace}</pre>
            )}
          </div>
        </section>
      )}

      {/* Actions */}
      <div className="details-actions">
        {canCancel && (
          <button className="btn btn-danger" onClick={handleCancel} disabled={isCancelling}>
            {isCancelling ? <span className="spinner" /> : <XCircleIcon />}
            Cancel Workflow
          </button>
        )}
        <button className="btn btn-secondary" onClick={onClose}>
          Close Details
        </button>
      </div>
    </div>
  );
}

function CheckIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="20 6 9 17 4 12" />
    </svg>
  );
}

function EditIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
      <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
    </svg>
  );
}

function XIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="18" y1="6" x2="6" y2="18" />
      <line x1="6" y1="6" x2="18" y2="18" />
    </svg>
  );
}

function XCircleIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10" />
      <line x1="15" y1="9" x2="9" y2="15" />
      <line x1="9" y1="9" x2="15" y2="15" />
    </svg>
  );
}
