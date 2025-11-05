// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Conversations;

namespace AgentGateway.UnitTests.Conversations;

/// <summary>
/// Tests for AgentConversationIndexGrain implementation.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public class AgentConversationIndexGrainTests
{
    private readonly OrleansTestClusterFixture _fixture;
    private IGrainFactory GrainFactory => this._fixture.GrainFactory;

    public AgentConversationIndexGrainTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;
    }

    private static string GetUniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    [Fact]
    public async Task AddConversationAsync_ShouldAddConversationIdAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        await grain.AddConversationAsync(conversationId, CancellationToken.None);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync(CancellationToken.None);
        conversationIds.Should().Contain(conversationId);
        conversationIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddConversationAsync_WhenCalledMultipleTimes_ShouldNotAddDuplicatesAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        await grain.AddConversationAsync(conversationId, CancellationToken.None);
        await grain.AddConversationAsync(conversationId, CancellationToken.None);
        await grain.AddConversationAsync(conversationId, CancellationToken.None);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync(CancellationToken.None);
        conversationIds.Should().Contain(conversationId);
        conversationIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddConversationAsync_ShouldAddMultipleConversationsAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        await grain.AddConversationAsync(conversationId1, CancellationToken.None);
        await grain.AddConversationAsync(conversationId2, CancellationToken.None);
        await grain.AddConversationAsync(conversationId3, CancellationToken.None);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync(CancellationToken.None);
        conversationIds.Should().Contain(conversationId1);
        conversationIds.Should().Contain(conversationId2);
        conversationIds.Should().Contain(conversationId3);
        conversationIds.Should().HaveCount(3);
    }

    [Fact]
    public async Task RemoveConversationAsync_ShouldRemoveExistingConversationAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        await grain.AddConversationAsync(conversationId, CancellationToken.None);

        // Act
        await grain.RemoveConversationAsync(conversationId, CancellationToken.None);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync(CancellationToken.None);
        conversationIds.Should().NotContain(conversationId);
        conversationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveConversationAsync_WhenConversationDoesNotExist_ShouldNotThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        await grain.RemoveConversationAsync(conversationId, CancellationToken.None);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync(CancellationToken.None);
        conversationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveConversationAsync_ShouldRemoveOnlySpecifiedConversationAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        await grain.AddConversationAsync(conversationId1, CancellationToken.None);
        await grain.AddConversationAsync(conversationId2, CancellationToken.None);
        await grain.AddConversationAsync(conversationId3, CancellationToken.None);

        // Act
        await grain.RemoveConversationAsync(conversationId2, CancellationToken.None);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync(CancellationToken.None);
        conversationIds.Should().Contain(conversationId1);
        conversationIds.Should().NotContain(conversationId2);
        conversationIds.Should().Contain(conversationId3);
        conversationIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetConversationIdsAsync_WhenEmpty_ShouldReturnEmptyListAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        var conversationIds = await grain.GetConversationIdsAsync(CancellationToken.None);

        // Assert
        conversationIds.Should().NotBeNull();
        conversationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConversationIdsAsync_ShouldReturnAllConversationsAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        await grain.AddConversationAsync(conversationId1, CancellationToken.None);
        await grain.AddConversationAsync(conversationId2, CancellationToken.None);
        await grain.AddConversationAsync(conversationId3, CancellationToken.None);

        // Act
        var conversationIds = await grain.GetConversationIdsAsync(CancellationToken.None);

        // Assert
        conversationIds.Should().HaveCount(3);
        conversationIds.Should().Contain(conversationId1);
        conversationIds.Should().Contain(conversationId2);
        conversationIds.Should().Contain(conversationId3);
    }
}
