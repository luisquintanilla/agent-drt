// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Conversations;

namespace AgentGateway.UnitTests.Conversations;

/// <summary>
/// Tests for OrleansAgentConversationIndex implementation.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public class OrleansAgentConversationIndexTests
{
    private readonly OrleansTestClusterFixture _fixture;
    private IGrainFactory GrainFactory => this._fixture.GrainFactory;

    public OrleansAgentConversationIndexTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;
    }

    private static string GetUniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    [Fact]
    public async Task AddConversationAsync_ShouldAddConversationAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act
        await index.AddConversationAsync(agentId, conversationId, CancellationToken.None);

        // Assert
        var conversationIds = await index.GetConversationIdsAsync(agentId, CancellationToken.None);
        conversationIds.Data.Should().Contain(conversationId);
    }

    [Fact]
    public async Task AddConversationAsync_WithNullAgentId_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.AddConversationAsync(null!, conversationId, CancellationToken.None));
    }

    [Fact]
    public async Task AddConversationAsync_WithEmptyAgentId_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.AddConversationAsync(string.Empty, conversationId, CancellationToken.None));
    }

    [Fact]
    public async Task AddConversationAsync_WithNullConversationId_ShouldThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.AddConversationAsync(agentId, null!, CancellationToken.None));
    }

    [Fact]
    public async Task AddConversationAsync_WithEmptyConversationId_ShouldThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.AddConversationAsync(agentId, string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task AddConversationAsync_ShouldAddToCorrectAgentAsync()
    {
        // Arrange
        var agentId1 = GetUniqueId("agent");
        var agentId2 = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act
        await index.AddConversationAsync(agentId1, conversationId1, CancellationToken.None);
        await index.AddConversationAsync(agentId2, conversationId2, CancellationToken.None);

        // Assert
        var conversationIds1 = await index.GetConversationIdsAsync(agentId1, CancellationToken.None);
        var conversationIds2 = await index.GetConversationIdsAsync(agentId2, CancellationToken.None);

        conversationIds1.Data.Should().Contain(conversationId1);
        conversationIds1.Data.Should().NotContain(conversationId2);

        conversationIds2.Data.Should().Contain(conversationId2);
        conversationIds2.Data.Should().NotContain(conversationId1);
    }

    [Fact]
    public async Task RemoveConversationAsync_ShouldRemoveConversationAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);
        await index.AddConversationAsync(agentId, conversationId, CancellationToken.None);

        // Act
        await index.RemoveConversationAsync(agentId, conversationId, CancellationToken.None);

        // Assert
        var conversationIds = await index.GetConversationIdsAsync(agentId, CancellationToken.None);
        conversationIds.Data.Should().NotContain(conversationId);
    }

    [Fact]
    public async Task RemoveConversationAsync_WithNullAgentId_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.RemoveConversationAsync(null!, conversationId, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveConversationAsync_WithEmptyAgentId_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.RemoveConversationAsync(string.Empty, conversationId, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveConversationAsync_WithNullConversationId_ShouldThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.RemoveConversationAsync(agentId, null!, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveConversationAsync_WithEmptyConversationId_ShouldThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.RemoveConversationAsync(agentId, string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task GetConversationIdsAsync_WhenEmpty_ShouldReturnEmptyListAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act
        var conversationIds = await index.GetConversationIdsAsync(agentId, CancellationToken.None);

        // Assert
        conversationIds.Should().NotBeNull();
        conversationIds.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConversationIdsAsync_WithNullAgentId_ShouldThrowAsync()
    {
        // Arrange
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.GetConversationIdsAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task GetConversationIdsAsync_WithEmptyAgentId_ShouldThrowAsync()
    {
        // Arrange
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.GetConversationIdsAsync(string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task GetConversationIdsAsync_ShouldReturnAllConversationsForAgentAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        await index.AddConversationAsync(agentId, conversationId1, CancellationToken.None);
        await index.AddConversationAsync(agentId, conversationId2, CancellationToken.None);
        await index.AddConversationAsync(agentId, conversationId3, CancellationToken.None);

        // Act
        var conversationIds = await index.GetConversationIdsAsync(agentId, CancellationToken.None);

        // Assert
        conversationIds.Data.Should().HaveCount(3);
        conversationIds.Data.Should().Contain(conversationId1);
        conversationIds.Data.Should().Contain(conversationId2);
        conversationIds.Data.Should().Contain(conversationId3);
    }

    [Fact]
    public async Task MultipleAgents_ShouldMaintainSeparateIndexesAsync()
    {
        // Arrange
        var agentId1 = GetUniqueId("agent");
        var agentId2 = GetUniqueId("agent");
        var agentId3 = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act
        await index.AddConversationAsync(agentId1, conversationId1, CancellationToken.None);
        await index.AddConversationAsync(agentId2, conversationId2, CancellationToken.None);
        await index.AddConversationAsync(agentId3, conversationId3, CancellationToken.None);

        // Assert
        var conversationIds1 = await index.GetConversationIdsAsync(agentId1, CancellationToken.None);
        var conversationIds2 = await index.GetConversationIdsAsync(agentId2, CancellationToken.None);
        var conversationIds3 = await index.GetConversationIdsAsync(agentId3, CancellationToken.None);

        conversationIds1.Data.Should().ContainSingle().Which.Should().Be(conversationId1);
        conversationIds2.Data.Should().ContainSingle().Which.Should().Be(conversationId2);
        conversationIds3.Data.Should().ContainSingle().Which.Should().Be(conversationId3);
    }
}
