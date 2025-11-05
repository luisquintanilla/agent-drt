// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Conversations;
using AgentGateway.Conversations.Models;
using AgentGateway.Models;
using AgentGateway.Responses.Models;

namespace AgentGateway.UnitTests.Conversations;

/// <summary>
/// Tests for ConversationGrain implementation.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public class ConversationGrainTests
{
    private readonly OrleansTestClusterFixture _fixture;
    private IGrainFactory GrainFactory => this._fixture.GrainFactory;

    public ConversationGrainTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;
    }

    private static string GetUniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    #region Conversation Tests

    [Fact]
    public async Task CreateAsync_ShouldCreateConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);

        // Act
        var result = await grain.CreateAsync(conversation, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(conversation.Id);
    }

    [Fact]
    public async Task CreateAsync_WhenAlreadyExists_ShouldThrowAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.CreateAsync(conversation, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_WhenExists_ShouldReturnConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        // Act
        var result = await grain.GetAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversation.Id);
    }

    [Fact]
    public async Task GetAsync_WhenDoesNotExist_ShouldReturnNullAsync()
    {
        // Arrange
        var grainId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(grainId);

        // Act
        var result = await grain.GetAsync(CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WhenExists_ShouldUpdateConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        var updatedMetadata = new Dictionary<string, string> { ["key"] = "value" };
        var updated = conversation with { Metadata = updatedMetadata };

        // Act
        var result = await grain.UpdateAsync(updated, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Metadata.Should().ContainKey("key");

        var retrieved = await grain.GetAsync(CancellationToken.None);
        retrieved!.Metadata.Should().ContainKey("key");
    }

    [Fact]
    public async Task UpdateAsync_WhenDoesNotExist_ShouldReturnNullAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);

        // Act
        var result = await grain.UpdateAsync(conversation, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenExists_ShouldDeleteConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        // Act
        var result = await grain.DeleteAsync(CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        var retrieved = await grain.GetAsync(CancellationToken.None);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenDoesNotExist_ShouldReturnFalseAsync()
    {
        // Arrange
        var grainId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(grainId);

        // Act
        var result = await grain.DeleteAsync(CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Item Tests

    [Fact]
    public async Task AddItemAsync_ShouldAddItemAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        var message = CreateTestMessage(conversation.Id);

        // Act
        var result = await grain.AddItemAsync(message, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task AddItemAsync_ToNonExistentConversation_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversationId);
        var message = CreateTestMessage(conversationId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.AddItemAsync(message, CancellationToken.None));
    }

    [Fact]
    public async Task AddItemAsync_WithDuplicateId_ShouldThrowAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        var message = CreateTestMessage(conversation.Id);
        await grain.AddItemAsync(message, CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.AddItemAsync(message, CancellationToken.None));
    }

    [Fact]
    public async Task GetItemAsync_WhenExists_ShouldReturnItemAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        var message = CreateTestMessage(conversation.Id);
        await grain.AddItemAsync(message, CancellationToken.None);

        // Act
        var result = await grain.GetItemAsync(message.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task GetItemAsync_WhenDoesNotExist_ShouldReturnNullAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        // Act
        var result = await grain.GetItemAsync(GetUniqueId("msg"), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListItemsAsync_ShouldReturnItemsInInsertionOrder_DescendingAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        // Add messages in specific order
        var msg1 = CreateTestMessage(conversation.Id, 1000);
        var msg2 = CreateTestMessage(conversation.Id, 2000);
        var msg3 = CreateTestMessage(conversation.Id, 3000);
        await grain.AddItemAsync(msg1, CancellationToken.None);
        await grain.AddItemAsync(msg2, CancellationToken.None);
        await grain.AddItemAsync(msg3, CancellationToken.None);

        // Act
        var result = await grain.ListItemsAsync(limit: 10, order: SortOrder.Descending, after: null, CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(3);
        result.Data[0].Id.Should().Be(msg3.Id);
        result.Data[1].Id.Should().Be(msg2.Id);
        result.Data[2].Id.Should().Be(msg1.Id);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task ListItemsAsync_ShouldReturnItemsInInsertionOrder_AscendingAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        var msg1 = CreateTestMessage(conversation.Id, 1000);
        var msg2 = CreateTestMessage(conversation.Id, 2000);
        var msg3 = CreateTestMessage(conversation.Id, 3000);
        await grain.AddItemAsync(msg1, CancellationToken.None);
        await grain.AddItemAsync(msg2, CancellationToken.None);
        await grain.AddItemAsync(msg3, CancellationToken.None);

        // Act
        var result = await grain.ListItemsAsync(limit: 10, order: SortOrder.Ascending, after: null, CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(3);
        result.Data[0].Id.Should().Be(msg1.Id);
        result.Data[1].Id.Should().Be(msg2.Id);
        result.Data[2].Id.Should().Be(msg3.Id);
    }

    [Fact]
    public async Task ListItemsAsync_WithLimit_ShouldReturnLimitedResultsAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        var messages = new List<ItemResource>();
        for (int i = 0; i < 5; i++)
        {
            var msg = CreateTestMessage(conversation.Id, i);
            messages.Add(msg);
            await grain.AddItemAsync(msg, CancellationToken.None);
        }

        // Act
        var result = await grain.ListItemsAsync(limit: 3, order: SortOrder.Descending, after: null, CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(3);
        result.HasMore.Should().BeTrue();
        result.Data[0].Id.Should().Be(messages[4].Id);
        result.Data[1].Id.Should().Be(messages[3].Id);
        result.Data[2].Id.Should().Be(messages[2].Id);
    }

    [Fact]
    public async Task ListItemsAsync_WithAfter_ShouldReturnItemsAfterCursorAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        var msg1 = CreateTestMessage(conversation.Id, 1000);
        var msg2 = CreateTestMessage(conversation.Id, 2000);
        var msg3 = CreateTestMessage(conversation.Id, 3000);
        await grain.AddItemAsync(msg1, CancellationToken.None);
        await grain.AddItemAsync(msg2, CancellationToken.None);
        await grain.AddItemAsync(msg3, CancellationToken.None);

        // Act - Get messages after msg-3 in descending order
        var result = await grain.ListItemsAsync(limit: 10, order: SortOrder.Descending, after: msg3.Id, CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(2);
        result.Data[0].Id.Should().Be(msg2.Id);
        result.Data[1].Id.Should().Be(msg1.Id);
    }

    [Fact]
    public async Task DeleteItemAsync_WhenExists_ShouldDeleteItemAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        var message = CreateTestMessage(conversation.Id);
        await grain.AddItemAsync(message, CancellationToken.None);

        // Act
        var result = await grain.DeleteItemAsync(message.Id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();

        var retrieved = await grain.GetItemAsync(message.Id, CancellationToken.None);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteItemAsync_WhenDoesNotExist_ShouldReturnFalseAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        // Act
        var result = await grain.DeleteItemAsync(GetUniqueId("msg"), CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ListItemsAsync_OrderedDictionary_ShouldMaintainInsertionOrderAsync()
    {
        // Arrange
        var conversation = CreateTestConversation();
        var grain = this.GrainFactory.GetGrain<IConversationGrain>(conversation.Id);
        await grain.CreateAsync(conversation, CancellationToken.None);

        // Add messages with non-sequential timestamps to verify insertion order
        var msgC = CreateTestMessage(conversation.Id, 3000);
        var msgA = CreateTestMessage(conversation.Id, 1000);
        var msgB = CreateTestMessage(conversation.Id, 2000);
        await grain.AddItemAsync(msgC, CancellationToken.None);
        await grain.AddItemAsync(msgA, CancellationToken.None);
        await grain.AddItemAsync(msgB, CancellationToken.None);

        // Act - Ascending should return in insertion order
        var result = await grain.ListItemsAsync(limit: 10, order: SortOrder.Ascending, after: null, CancellationToken.None);

        // Assert - Should be in insertion order, not sorted by timestamp
        result.Data.Should().HaveCount(3);
        result.Data[0].Id.Should().Be(msgC.Id);
        result.Data[1].Id.Should().Be(msgA.Id);
        result.Data[2].Id.Should().Be(msgB.Id);
    }

    #endregion

    #region Helper Methods

    private static Conversation CreateTestConversation(long createdAt = 1000)
    {
        var id = GetUniqueId("conv");
        return new Conversation
        {
            Id = id,
            CreatedAt = createdAt,
            Metadata = new Dictionary<string, string>()
        };
    }

    private static ResponsesUserMessageItemResource CreateTestMessage(string conversationId, long createdAt = 1000)
    {
        var id = GetUniqueId("msg");
        return new ResponsesUserMessageItemResource
        {
            Id = id,
            Content = new List<ItemContent>
            {
                new ItemContentInputText { Text = "Test message" }
            },
            Status = ResponsesMessageItemResourceStatus.Completed
        };
    }

    #endregion
}
