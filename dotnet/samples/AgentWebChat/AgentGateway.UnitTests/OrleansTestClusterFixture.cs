// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans.Serialization;
using Orleans.Storage;
using Orleans.TestingHost;

namespace AgentGateway.UnitTests;

/// <summary>
/// xUnit collection fixture for Orleans test cluster.
/// This ensures a single cluster instance is shared across all tests in the collection.
/// </summary>
public sealed class OrleansTestClusterFixture : IAsyncLifetime
{
    /// <summary>
    /// Gets the Orleans test cluster instance.
    /// </summary>
    public InProcessTestCluster Cluster { get; private set; } = null!;

    /// <summary>
    /// Gets the grain factory for creating grain references.
    /// </summary>
    public IGrainFactory GrainFactory => this.Cluster.Client;

    /// <summary>
    /// Gets the mock IResponseExecutor instance shared across all tests.
    /// Reset this mock between tests using ResetResponseExecutorMock().
    /// </summary>
    internal Mock<IResponseExecutor> ResponseExecutorMock { get; private set; } = null!;

    /// <summary>
    /// Resets the mock IResponseExecutor to clear any previous setups or verifications.
    /// Call this in test constructors or setup methods to ensure test isolation.
    /// </summary>
    public void ResetResponseExecutorMock()
    {
        this.ResponseExecutorMock.Reset();
    }

    /// <summary>
    /// Sets up a default response for the mock IResponseExecutor.
    /// This provides a minimal streaming response that will satisfy tests that just need execution to complete.
    /// Tests that need specific response behavior should set up their own mock.
    /// </summary>
    public void SetupDefaultResponseExecutor()
    {
        // Default implementation returns minimal stream to allow grain to complete
        this.ResponseExecutorMock
            .Setup(x => x.ExecuteAsync(
                It.IsAny<AgentInvocationContext>(),
                It.IsAny<CreateResponse>(),
                It.IsAny<CancellationToken>()))
            .Returns((AgentInvocationContext ctx,
                     CreateResponse req,
                     CancellationToken ct) => GetMinimalStreamingResponseEventsAsync(ctx, req));
    }

    private static async IAsyncEnumerable<StreamingResponseEvent> GetMinimalStreamingResponseEventsAsync(
        AgentInvocationContext context,
        CreateResponse request)
    {
        // Return minimal events to allow the response to complete
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Emit response.created event
        yield return new StreamingResponseCreated
        {
            SequenceNumber = 1,
            Response = new Response
            {
                Id = context.ResponseId,
                Status = ResponseStatus.InProgress,
                CreatedAt = createdAt,
                Model = request.Model,
                Instructions = request.Instructions,
                Output = [],
                Usage = new ResponseUsage
                {
                    InputTokens = 0,
                    OutputTokens = 0,
                    TotalTokens = 0,
                    InputTokensDetails = new InputTokensDetails
                    {
                        CachedTokens = 0
                    },
                    OutputTokensDetails = new OutputTokensDetails
                    {
                        ReasoningTokens = 0
                    }
                },
                Tools = []
            }
        };

        // Emit response.completed event with empty output
        yield return new StreamingResponseCompleted
        {
            SequenceNumber = 2,
            Response = new Response
            {
                Id = context.ResponseId,
                Status = ResponseStatus.Completed,
                CreatedAt = createdAt,
                Model = request.Model,
                Instructions = request.Instructions,
                Output = [],
                Usage = new ResponseUsage
                {
                    InputTokens = 0,
                    OutputTokens = 0,
                    TotalTokens = 0,
                    InputTokensDetails = new InputTokensDetails
                    {
                        CachedTokens = 0
                    },
                    OutputTokensDetails = new OutputTokensDetails
                    {
                        ReasoningTokens = 0
                    }
                },
                Tools = []
            }
        };

        await Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        // Create the shared mock IResponseExecutor
        this.ResponseExecutorMock = new Mock<IResponseExecutor>();

        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureHost(hostBuilder =>
        {
            // Configure System.Text.Json serialization for all Microsoft.Agents.* and AgentGateway types
            // This uses AgentGatewayJsonUtilities which chains together all the necessary type resolvers
            // including OpenAIJsonUtilities (OpenAI Hosting types), AIJsonUtilities (Microsoft.Extensions.AI), and grain states
            hostBuilder.Services.AddSerializer(serializerBuilder =>
            {
                // Support all Microsoft.Agents.*, AgentContracts, and AgentGateway types using AgentGatewayJsonUtilities
                // which includes proper type resolver chaining for:
                // - Grain state types (ConversationState, ResponseState, AgentConversationIndexState)
                // - OpenAI Hosting types (Conversation, ItemResource, Response, etc.)
                // - Microsoft.Extensions.AI types (AIContent, ChatMessage, etc.)
                // - AgentContracts types (via AgentContractsJsonUtilities)
                // NOTE: Exceptions are excluded to allow Orleans to use its built-in exception serialization
                serializerBuilder.AddJsonSerializer(
                    isSupported: type => !typeof(Exception).IsAssignableFrom(type) &&
                                        (type.Namespace?.StartsWith("Microsoft.Agents", StringComparison.Ordinal) == true ||
                                         type.Namespace?.StartsWith("AgentContracts", StringComparison.Ordinal) == true ||
                                         type.Namespace?.StartsWith("AgentGateway", StringComparison.Ordinal) == true),
                    jsonSerializerOptions: AgentGatewayJsonUtilities.DefaultOptions);
            });
        })
        .ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.UseInMemoryReminderService();

            // Register System.Text.Json-based grain storage serializer
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer>(sp =>
                new Utilities.SystemTextJsonGrainStorageSerializer(AgentGatewayJsonUtilities.DefaultOptions));

            // Register the shared mock IResponseExecutor for testing
            siloBuilder.Services.AddSingleton(_ => this.ResponseExecutorMock.Object);
        });
        this.Cluster = builder.Build();
        await this.Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (this.Cluster != null)
        {
            await this.Cluster.StopAllSilosAsync();
            this.Cluster.Dispose();
        }
    }
}

/// <summary>
/// xUnit collection definition for Orleans test cluster.
/// All test classes that need an Orleans cluster should use this collection.
/// </summary>
[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "This is the standard xUnit collection pattern")]
public sealed class OrleansClusterCollection : ICollectionFixture<OrleansTestClusterFixture>
{
    public const string Name = nameof(OrleansClusterCollection);
}
