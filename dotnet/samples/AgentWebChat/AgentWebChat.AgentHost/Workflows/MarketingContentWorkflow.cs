// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentContracts.Workflows;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// A sample Human-in-the-Loop (HITL) workflow for marketing content creation.
/// The workflow consists of three steps:
/// 1. Writer step - generates initial marketing content
/// 2. Reviewer step - reviews and refines the content
/// 3. Human Approval step - pauses for human approval (HITL)
/// </summary>
public sealed class MarketingContentWorkflow : IWorkflow
{
    private const string WriterStepId = "writer";
    private const string WriterExecutorId = "ai-writer";
    private const string ReviewerStepId = "reviewer";
    private const string ReviewerExecutorId = "ai-reviewer";
    private const string ApprovalStepId = "approval";
    private const string ApprovalExecutorId = "human-approval";
    private const string ApprovalPortId = "approval-port";

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkflowStatusEvent> ExecuteAsync(
        WorkflowExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sequenceNumber = 0;

        context.Logger.LogInformation("Starting marketing content workflow: {RunId}", context.RunId);

        // === Step 1: Writer ===
        var writerStartedAt = DateTimeOffset.UtcNow;
        var writerStep = new WorkflowStepStartedRecord
        {
            StepId = WriterStepId,
            ExecutorId = WriterExecutorId,
            ExecutorName = "AI Content Writer",
            StartedAt = writerStartedAt
        };

        await context.StateService.RecordStepStartedAsync(context.RunId, writerStep, etag: null, cancellationToken);
        yield return new WorkflowStepStartedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Step = writerStep
        };

        // Simulate writer generating content
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var writerOutput = GenerateWriterContent(context.Input);

        var writerCompletedAt = DateTimeOffset.UtcNow;
        var writerCompleted = new WorkflowStepCompletedRecord
        {
            StepId = WriterStepId,
            ExecutorId = WriterExecutorId,
            CompletedAt = writerCompletedAt,
            Output = writerOutput,
            DurationMs = (long)(writerCompletedAt - writerStartedAt).TotalMilliseconds
        };

        await context.StateService.RecordStepCompletedAsync(context.RunId, writerCompleted, etag: null, cancellationToken);
        yield return new WorkflowStepCompletedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Step = writerCompleted
        };

        // === Step 2: Reviewer ===
        var reviewerStartedAt = DateTimeOffset.UtcNow;
        var reviewerStep = new WorkflowStepStartedRecord
        {
            StepId = ReviewerStepId,
            ExecutorId = ReviewerExecutorId,
            ExecutorName = "AI Content Reviewer",
            StartedAt = reviewerStartedAt
        };

        await context.StateService.RecordStepStartedAsync(context.RunId, reviewerStep, etag: null, cancellationToken);
        yield return new WorkflowStepStartedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Step = reviewerStep
        };

        // Simulate reviewer refining content
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var reviewerOutput = GenerateReviewerContent(writerOutput);

        var reviewerCompletedAt = DateTimeOffset.UtcNow;
        var reviewerCompleted = new WorkflowStepCompletedRecord
        {
            StepId = ReviewerStepId,
            ExecutorId = ReviewerExecutorId,
            CompletedAt = reviewerCompletedAt,
            Output = reviewerOutput,
            DurationMs = (long)(reviewerCompletedAt - reviewerStartedAt).TotalMilliseconds
        };

        await context.StateService.RecordStepCompletedAsync(context.RunId, reviewerCompleted, etag: null, cancellationToken);
        yield return new WorkflowStepCompletedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Step = reviewerCompleted
        };

        // === Step 3: Human Approval (HITL) ===
        var approvalStartedAt = DateTimeOffset.UtcNow;
        var approvalStep = new WorkflowStepStartedRecord
        {
            StepId = ApprovalStepId,
            ExecutorId = ApprovalExecutorId,
            ExecutorName = "Human Approval",
            StartedAt = approvalStartedAt
        };

        await context.StateService.RecordStepStartedAsync(context.RunId, approvalStep, etag: null, cancellationToken);
        yield return new WorkflowStepStartedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Step = approvalStep
        };

        // Create pending request for human approval
        var requestId = $"approval_{Guid.NewGuid():N}";
        var reviewedContent = reviewerOutput.As<ContentData>()?.Content ?? "Content not available.";

        var pendingRequest = new PendingExternalRequest
        {
            RequestId = requestId,
            PortId = ApprovalPortId,
            RequestTypeName = typeof(ApprovalRequest).FullName ?? nameof(ApprovalRequest),
            ResponseTypeName = typeof(ApprovalResponse).FullName ?? nameof(ApprovalResponse),
            RequestData = WorkflowMessage.Create(new ApprovalRequest
            {
                Content = reviewedContent,
                Options = ["approve", "reject", "revise"]
            }),
            Title = "Content Approval Required",
            Description = "Please review the marketing content and approve, reject, or request revisions.",
            UIHints = new Dictionary<string, string>
            {
                ["renderAs"] = "approval_dialog",
                ["primaryAction"] = "approve",
                ["secondaryActions"] = "reject,revise"
            },
            RequestedAt = DateTimeOffset.UtcNow
        };

        // Save checkpoint for resumption
        var checkpointData = new WorkflowCheckpointData
        {
            CheckpointId = $"cp_{Guid.NewGuid():N}",
            Data = JsonSerializer.SerializeToUtf8Bytes(new MarketingWorkflowCheckpoint
            {
                OriginalInput = context.Input,
                WriterOutput = writerOutput,
                ReviewerOutput = reviewerOutput,
                ApprovalRequestId = requestId,
                ApprovalStepStartedAt = approvalStartedAt
            }),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await context.StateService.SaveCheckpointAsync(context.RunId, checkpointData, etag: null, cancellationToken);
        await context.StateService.RecordPendingRequestAsync(context.RunId, pendingRequest, etag: null, cancellationToken);

        // Emit signal requested event (workflow pauses here)
        yield return new WorkflowSignalRequestedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Request = pendingRequest
        };

        // Workflow execution stops here - will be resumed via ResumeAsync
        context.Logger.LogInformation("Workflow paused for human approval: {RunId}, RequestId: {RequestId}",
            context.RunId, requestId);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkflowStatusEvent> ResumeAsync(
        WorkflowResumeContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sequenceNumber = 100; // Start from a high number to avoid collisions

        context.Logger.LogInformation("Resuming marketing content workflow: {RunId}, Signal: {RequestId}",
            context.RunId, context.Signal.RequestId);

        // Parse checkpoint data
        MarketingWorkflowCheckpoint? checkpoint = null;
        if (context.Checkpoint?.Data is { Length: > 0 } checkpointBytes)
        {
            checkpoint = JsonSerializer.Deserialize<MarketingWorkflowCheckpoint>(checkpointBytes);
        }

        // Parse the signal response
        var approvalResponse = context.Signal.Response.As<ApprovalResponse>();
        var decision = approvalResponse?.Decision ?? "reject";
        var feedback = approvalResponse?.Feedback;

        // Normalize decision for comparison
        var isApprove = string.Equals(decision, "approve", StringComparison.OrdinalIgnoreCase);
        var isRevise = string.Equals(decision, "revise", StringComparison.OrdinalIgnoreCase);

        context.Logger.LogInformation("Human decision: {Decision}, Feedback: {Feedback}", decision, feedback);

        // Emit signal received event
        yield return new WorkflowSignalReceivedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            RequestId = context.Signal.RequestId,
            Response = context.Signal.Response
        };

        // Complete the approval step
        var approvalCompletedAt = DateTimeOffset.UtcNow;
        var approvalCompleted = new WorkflowStepCompletedRecord
        {
            StepId = ApprovalStepId,
            ExecutorId = ApprovalExecutorId,
            CompletedAt = approvalCompletedAt,
            Output = WorkflowMessage.Create(approvalResponse ?? new ApprovalResponse { Decision = decision }),
            DurationMs = checkpoint != null
                ? (long)(approvalCompletedAt - checkpoint.ApprovalStepStartedAt).TotalMilliseconds
                : 0
        };

        await context.StateService.RecordStepCompletedAsync(context.RunId, approvalCompleted, etag: null, cancellationToken);
        yield return new WorkflowStepCompletedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Step = approvalCompleted
        };

        if (isRevise)
        {
            // === REVISION LOOP: Re-run writer and reviewer with feedback ===
            var revisionNumber = (checkpoint?.RevisionNumber ?? 0) + 1;
            context.Logger.LogInformation("Starting revision {RevisionNumber} for workflow {RunId}", revisionNumber, context.RunId);

            // Record revision artifact for visibility
            var revisionArtifact = new WorkflowArtifactRecord
            {
                Id = $"revision_request_{Guid.NewGuid():N}",
                Name = $"Revision Request #{revisionNumber}",
                ContentType = "text/plain",
                Content = $"Revision requested. Feedback: {feedback ?? "No specific feedback provided."}",
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = "revision_requested",
                    ["revision_number"] = revisionNumber.ToString(),
                    ["feedback"] = feedback ?? string.Empty
                }
            };

            await context.StateService.RecordArtifactAsync(context.RunId, revisionArtifact, etag: null, cancellationToken);
            yield return new WorkflowArtifactCreatedEvent
            {
                RunId = context.RunId,
                SequenceNumber = ++sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Artifact = revisionArtifact
            };

            // === Re-run Writer with feedback ===
            var writerStepId = $"{WriterStepId}_v{revisionNumber}";
            var writerStartedAt = DateTimeOffset.UtcNow;
            var writerStep = new WorkflowStepStartedRecord
            {
                StepId = writerStepId,
                ExecutorId = WriterExecutorId,
                ExecutorName = $"AI Content Writer (Revision {revisionNumber})",
                StartedAt = writerStartedAt
            };

            await context.StateService.RecordStepStartedAsync(context.RunId, writerStep, etag: null, cancellationToken);
            yield return new WorkflowStepStartedEvent
            {
                RunId = context.RunId,
                SequenceNumber = ++sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Step = writerStep
            };

            // Simulate writer generating revised content
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            var previousContent = checkpoint?.ReviewerOutput?.As<ContentData>()?.Content ?? "";
            var originalInput = checkpoint?.OriginalInput ?? WorkflowMessage.Create(new ContentRequest { Topic = "general marketing" });
            var writerOutput = GenerateRevisedWriterContent(originalInput, feedback, previousContent, revisionNumber);

            var writerCompletedAt = DateTimeOffset.UtcNow;
            var writerCompleted = new WorkflowStepCompletedRecord
            {
                StepId = writerStepId,
                ExecutorId = WriterExecutorId,
                CompletedAt = writerCompletedAt,
                Output = writerOutput,
                DurationMs = (long)(writerCompletedAt - writerStartedAt).TotalMilliseconds
            };

            await context.StateService.RecordStepCompletedAsync(context.RunId, writerCompleted, etag: null, cancellationToken);
            yield return new WorkflowStepCompletedEvent
            {
                RunId = context.RunId,
                SequenceNumber = ++sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Step = writerCompleted
            };

            // === Re-run Reviewer ===
            var reviewerStepId = $"{ReviewerStepId}_v{revisionNumber}";
            var reviewerStartedAt = DateTimeOffset.UtcNow;
            var reviewerStep = new WorkflowStepStartedRecord
            {
                StepId = reviewerStepId,
                ExecutorId = ReviewerExecutorId,
                ExecutorName = $"AI Content Reviewer (Revision {revisionNumber})",
                StartedAt = reviewerStartedAt
            };

            await context.StateService.RecordStepStartedAsync(context.RunId, reviewerStep, etag: null, cancellationToken);
            yield return new WorkflowStepStartedEvent
            {
                RunId = context.RunId,
                SequenceNumber = ++sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Step = reviewerStep
            };

            // Simulate reviewer refining revised content
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            var reviewerOutput = GenerateReviewerContent(writerOutput);

            var reviewerCompletedAt = DateTimeOffset.UtcNow;
            var reviewerCompleted = new WorkflowStepCompletedRecord
            {
                StepId = reviewerStepId,
                ExecutorId = ReviewerExecutorId,
                CompletedAt = reviewerCompletedAt,
                Output = reviewerOutput,
                DurationMs = (long)(reviewerCompletedAt - reviewerStartedAt).TotalMilliseconds
            };

            await context.StateService.RecordStepCompletedAsync(context.RunId, reviewerCompleted, etag: null, cancellationToken);
            yield return new WorkflowStepCompletedEvent
            {
                RunId = context.RunId,
                SequenceNumber = ++sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Step = reviewerCompleted
            };

            // === Request approval again ===
            var approvalStepId = $"{ApprovalStepId}_v{revisionNumber}";
            var newApprovalStartedAt = DateTimeOffset.UtcNow;
            var newApprovalStep = new WorkflowStepStartedRecord
            {
                StepId = approvalStepId,
                ExecutorId = ApprovalExecutorId,
                ExecutorName = $"Human Approval (Revision {revisionNumber})",
                StartedAt = newApprovalStartedAt
            };

            await context.StateService.RecordStepStartedAsync(context.RunId, newApprovalStep, etag: null, cancellationToken);
            yield return new WorkflowStepStartedEvent
            {
                RunId = context.RunId,
                SequenceNumber = ++sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Step = newApprovalStep
            };

            // Create new pending request for human approval
            var requestId = $"approval_{Guid.NewGuid():N}";
            var reviewedContent = reviewerOutput.As<ContentData>()?.Content ?? "Content not available.";

            var pendingRequest = new PendingExternalRequest
            {
                RequestId = requestId,
                PortId = ApprovalPortId,
                RequestTypeName = typeof(ApprovalRequest).FullName ?? nameof(ApprovalRequest),
                ResponseTypeName = typeof(ApprovalResponse).FullName ?? nameof(ApprovalResponse),
                RequestData = WorkflowMessage.Create(new ApprovalRequest
                {
                    Content = reviewedContent,
                    Options = ["approve", "reject", "revise"]
                }),
                Title = $"Content Approval Required (Revision {revisionNumber})",
                Description = $"Please review the revised marketing content (revision {revisionNumber}) and approve, reject, or request further revisions.",
                UIHints = new Dictionary<string, string>
                {
                    ["renderAs"] = "approval_dialog",
                    ["primaryAction"] = "approve",
                    ["secondaryActions"] = "reject,revise"
                },
                RequestedAt = DateTimeOffset.UtcNow
            };

            // Save checkpoint for next resumption
            var newCheckpointData = new WorkflowCheckpointData
            {
                CheckpointId = $"cp_{Guid.NewGuid():N}",
                Data = JsonSerializer.SerializeToUtf8Bytes(new MarketingWorkflowCheckpoint
                {
                    OriginalInput = checkpoint?.OriginalInput,
                    WriterOutput = writerOutput,
                    ReviewerOutput = reviewerOutput,
                    ApprovalRequestId = requestId,
                    ApprovalStepStartedAt = newApprovalStartedAt,
                    RevisionNumber = revisionNumber
                }),
                CreatedAt = DateTimeOffset.UtcNow
            };

            await context.StateService.SaveCheckpointAsync(context.RunId, newCheckpointData, etag: null, cancellationToken);
            await context.StateService.RecordPendingRequestAsync(context.RunId, pendingRequest, etag: null, cancellationToken);

            // Emit signal requested event (workflow pauses again)
            yield return new WorkflowSignalRequestedEvent
            {
                RunId = context.RunId,
                SequenceNumber = ++sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow,
                Request = pendingRequest
            };

            context.Logger.LogInformation("Workflow paused for human approval (revision {RevisionNumber}): {RunId}, RequestId: {RequestId}",
                revisionNumber, context.RunId, requestId);

            // Exit - workflow will be resumed again via ResumeAsync when signal is received
            yield break;
        }

        // === FINAL STATE: Approve or Reject ===
        var reviewedContentFinal = checkpoint?.ReviewerOutput?.As<ContentData>()?.Content ?? "Content not available.";
        WorkflowArtifactRecord artifact;

        if (isApprove)
        {
            artifact = new WorkflowArtifactRecord
            {
                Id = $"final_{Guid.NewGuid():N}",
                Name = "Approved Marketing Content",
                ContentType = "text/markdown",
                Content = reviewedContentFinal,
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = "approved",
                    ["approved_by"] = "human_reviewer",
                    ["revision_count"] = (checkpoint?.RevisionNumber ?? 0).ToString()
                }
            };
        }
        else
        {
            artifact = new WorkflowArtifactRecord
            {
                Id = $"rejected_{Guid.NewGuid():N}",
                Name = "Rejection Notice",
                ContentType = "text/plain",
                Content = $"Content was rejected. Feedback: {feedback ?? "No specific feedback provided."}",
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = "rejected",
                    ["feedback"] = feedback ?? string.Empty,
                    ["revision_count"] = (checkpoint?.RevisionNumber ?? 0).ToString()
                }
            };
        }

        await context.StateService.RecordArtifactAsync(context.RunId, artifact, etag: null, cancellationToken);
        yield return new WorkflowArtifactCreatedEvent
        {
            RunId = context.RunId,
            SequenceNumber = ++sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            Artifact = artifact
        };

        context.Logger.LogInformation("Marketing content workflow completed: {RunId}, Decision: {Decision}",
            context.RunId, decision);
    }

    private static WorkflowMessage GenerateWriterContent(WorkflowMessage input)
    {
        var inputData = input.As<ContentRequest>();
        var topic = inputData?.Topic ?? "general marketing";

        var content = $"""
            # Marketing Content Draft

            **Topic:** {topic}

            ## Headline
            Discover the Future of {topic} - Transform Your Business Today!

            ## Body
            Are you ready to take your business to the next level? Our innovative solution 
            offers unparalleled benefits that will revolutionize the way you approach {topic}.

            Key benefits:
            - Increased efficiency by up to 50%
            - Seamless integration with existing systems
            - 24/7 support from our expert team
            - Proven results from industry leaders

            Don't miss this opportunity to stay ahead of the competition!

            ## Call to Action
            Contact us today for a free consultation and see how we can help you succeed.

            ---
            *Draft generated by AI Writer*
            """;

        return WorkflowMessage.Create(new ContentData
        {
            Content = content,
            Step = "writer",
            Version = "1.0"
        });
    }

    private static WorkflowMessage GenerateReviewerContent(WorkflowMessage writerOutput)
    {
        var writerData = writerOutput.As<ContentData>();
        var originalContent = writerData?.Content ?? string.Empty;

        var reviewedContent = $"""
            {originalContent}

            ---
            ## Reviewer Notes

            **Quality Score:** 8.5/10

            **Strengths:**
            - Clear and compelling headline
            - Well-structured content with bullet points
            - Strong call to action

            **Suggestions:**
            - Consider adding specific metrics or case studies
            - The tone could be slightly more professional
            - Add social proof or testimonials

            **Recommendation:** Ready for human approval with minor adjustments.

            ---
            *Reviewed by AI Reviewer*
            """;

        return WorkflowMessage.Create(new ContentData
        {
            Content = reviewedContent,
            Step = "reviewer",
            Version = "1.0",
            QualityScore = 8.5
        });
    }

    private static WorkflowMessage GenerateRevisedWriterContent(WorkflowMessage input, string? feedback, string previousContent, int revisionNumber)
    {
        var inputData = input.As<ContentRequest>();
        var topic = inputData?.Topic ?? "general marketing";

        // Simulate incorporating feedback into revised content
        var feedbackNote = string.IsNullOrEmpty(feedback)
            ? "general improvements"
            : feedback;

        var content = $"""
            # Marketing Content Draft (Revision {revisionNumber})

            **Topic:** {topic}

            > **Revision Note:** This version addresses the following feedback: "{feedbackNote}"

            ## Headline
            Unlock the Power of {topic} - Your Success Story Starts Here!

            ## Body
            Transform your business with our proven {topic} solutions. We've listened to your feedback 
            and refined our approach to deliver even greater value.

            **Why Choose Us:**
            - **Measurable Results:** Our clients see an average 47% improvement in efficiency
            - **Seamless Integration:** Works with your existing tools and workflows
            - **Expert Support:** Dedicated team available 24/7
            - **Trusted by Leaders:** Join 500+ industry-leading companies

            **Client Testimonial:**
            > "This solution transformed how we approach {topic}. Highly recommended!" 
            > — Sarah M., VP of Marketing

            ## Call to Action
            Ready to see the difference? Schedule your free demo today and discover 
            how we can accelerate your success.

            ---
            *Revision {revisionNumber} - Generated by AI Writer based on reviewer feedback*
            """;

        return WorkflowMessage.Create(new ContentData
        {
            Content = content,
            Step = "writer",
            Version = $"1.{revisionNumber}"
        });
    }
}

// ============ Workflow-specific data types ============

/// <summary>
/// Request to create marketing content.
/// </summary>
public sealed class ContentRequest
{
    [JsonPropertyName("topic")]
    public string? Topic { get; init; }

    [JsonPropertyName("targetAudience")]
    public string? TargetAudience { get; init; }

    [JsonPropertyName("tone")]
    public string? Tone { get; init; }
}

/// <summary>
/// Content data generated by the workflow steps.
/// </summary>
public sealed class ContentData
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("step")]
    public string? Step { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("qualityScore")]
    public double? QualityScore { get; init; }
}

/// <summary>
/// Request for human approval.
/// </summary>
public sealed class ApprovalRequest
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("options")]
    public IReadOnlyList<string>? Options { get; init; }
}

/// <summary>
/// Human approval response.
/// </summary>
public sealed class ApprovalResponse
{
    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("feedback")]
    public string? Feedback { get; init; }
}

/// <summary>
/// Checkpoint data for the marketing workflow to enable resumption.
/// </summary>
internal sealed class MarketingWorkflowCheckpoint
{
    public WorkflowMessage? OriginalInput { get; init; }
    public WorkflowMessage? WriterOutput { get; init; }
    public WorkflowMessage? ReviewerOutput { get; init; }
    public string? ApprovalRequestId { get; init; }
    public DateTimeOffset ApprovalStepStartedAt { get; init; }
    public int RevisionNumber { get; init; }
}
