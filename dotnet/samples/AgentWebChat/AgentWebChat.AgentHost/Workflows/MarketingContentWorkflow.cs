// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
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

        // Create final artifact based on decision
        var reviewedContent = checkpoint?.ReviewerOutput?.As<ContentData>()?.Content ?? "Content not available.";
        WorkflowArtifactRecord artifact;

        if (isApprove)
        {
            artifact = new WorkflowArtifactRecord
            {
                Id = $"final_{Guid.NewGuid():N}",
                Name = "Approved Marketing Content",
                ContentType = "text/markdown",
                Content = reviewedContent,
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = "approved",
                    ["approved_by"] = "human_reviewer"
                }
            };
        }
        else if (isRevise)
        {
            // In a real workflow, we might loop back to the writer step
            // For simplicity, we just create a "needs revision" artifact
            artifact = new WorkflowArtifactRecord
            {
                Id = $"revision_{Guid.NewGuid():N}",
                Name = "Revision Request",
                ContentType = "text/plain",
                Content = $"Content requires revision. Feedback: {feedback ?? "No specific feedback provided."}",
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = "revision_requested",
                    ["feedback"] = feedback ?? string.Empty
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
                    ["feedback"] = feedback ?? string.Empty
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
}

// ============ Workflow-specific data types ============

/// <summary>
/// Request to create marketing content.
/// </summary>
public sealed class ContentRequest
{
    public string? Topic { get; init; }
    public string? TargetAudience { get; init; }
    public string? Tone { get; init; }
}

/// <summary>
/// Content data generated by the workflow steps.
/// </summary>
public sealed class ContentData
{
    public string? Content { get; init; }
    public string? Step { get; init; }
    public string? Version { get; init; }
    public double? QualityScore { get; init; }
}

/// <summary>
/// Request for human approval.
/// </summary>
public sealed class ApprovalRequest
{
    public string? Content { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
}

/// <summary>
/// Human approval response.
/// </summary>
public sealed class ApprovalResponse
{
    public string? Decision { get; init; }
    public string? Feedback { get; init; }
}

/// <summary>
/// Checkpoint data for the marketing workflow to enable resumption.
/// </summary>
internal sealed class MarketingWorkflowCheckpoint
{
    public WorkflowMessage? WriterOutput { get; init; }
    public WorkflowMessage? ReviewerOutput { get; init; }
    public string? ApprovalRequestId { get; init; }
    public DateTimeOffset ApprovalStepStartedAt { get; init; }
}
