import { useState } from 'react';
import { workflowApi, createWorkflowMessage } from '../api';
import type { ContentRequest } from '../types';
import './StartWorkflowForm.css';

interface StartWorkflowFormProps {
  onWorkflowStarted: () => void;
  onError: (message: string) => void;
}

export function StartWorkflowForm({ onWorkflowStarted, onError }: StartWorkflowFormProps) {
  const [topic, setTopic] = useState('');
  const [audience, setAudience] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!topic.trim()) {
      onError('Please enter a topic');
      return;
    }

    setIsSubmitting(true);

    try {
      const input: ContentRequest = {
        topic: topic.trim(),
        targetAudience: audience.trim() || undefined,
      };

      await workflowApi.startWorkflow({
        workflowName: 'MarketingContentWorkflow',
        input: createWorkflowMessage(input),
      });

      setTopic('');
      setAudience('');
      onWorkflowStarted();
    } catch (error) {
      onError(error instanceof Error ? error.message : 'Failed to start workflow');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="start-workflow-form">
      <h2>Start New Marketing Content Workflow</h2>
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="topic">Topic *</label>
          <input
            id="topic"
            type="text"
            value={topic}
            onChange={(e) => setTopic(e.target.value)}
            placeholder="e.g., AI-powered customer service"
            disabled={isSubmitting}
          />
        </div>
        <div className="form-group">
          <label htmlFor="audience">Target Audience (optional)</label>
          <input
            id="audience"
            type="text"
            value={audience}
            onChange={(e) => setAudience(e.target.value)}
            placeholder="e.g., Enterprise customers"
            disabled={isSubmitting}
          />
        </div>
        <button type="submit" className="btn btn-primary" disabled={isSubmitting || !topic.trim()}>
          {isSubmitting ? (
            <>
              <span className="spinner" />
              Starting...
            </>
          ) : (
            <>
              <PlayIcon />
              Start Workflow
            </>
          )}
        </button>
      </form>
    </div>
  );
}

function PlayIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polygon points="5 3 19 12 5 21 5 3" />
    </svg>
  );
}
