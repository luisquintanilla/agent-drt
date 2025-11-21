// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace AgentGateway.UnitTests.Conversations;

/// <summary>
/// Tests for OrleansConversationStorage implementation.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public class OrleansConversationStorageTests
{
    private readonly OrleansConversationStorage _storage;

    public OrleansConversationStorageTests(OrleansTestClusterFixture fixture)
    {
        this._storage = new OrleansConversationStorage(fixture.GrainFactory);
    }

    private static string GetUniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    #region Conversation Tests

    [Fact]
    public async Task CreateConversationAsync_ShouldCreateConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation(GetUniqueId("conv"));

        // Act
        var result = await this._storage!.CreateConversationAsync(conversation);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(conversation.Id);
        result.CreatedAt.Should().Be(conversation.CreatedAt);
    }

    [Fact]
    public async Task CreateConversationAsync_WithDuplicateId_ShouldThrowAsync()
    {
        // Arrange
        var conversation = CreateTestConversation(GetUniqueId("conv"));
        await this._storage!.CreateConversationAsync(conversation);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._storage.CreateConversationAsync(conversation));
    }

    [Fact]
    public async Task GetConversationAsync_ExistingConversation_ShouldReturnConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation(GetUniqueId("conv"));
        await this._storage!.CreateConversationAsync(conversation);

        // Act
        var result = await this._storage.GetConversationAsync(conversation.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversation.Id);
    }

    [Fact]
    public async Task GetConversationAsync_NonExistentConversation_ShouldReturnNullAsync()
    {
        // Act
        var result = await this._storage!.GetConversationAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateConversationAsync_ExistingConversation_ShouldUpdateConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation(GetUniqueId("conv"));
        await this._storage!.CreateConversationAsync(conversation);

        var updatedMetadata = new Dictionary<string, string> { ["key"] = "value" };
        var updatedConversation = conversation with { Metadata = updatedMetadata };

        // Act
        var result = await this._storage.UpdateConversationAsync(updatedConversation);

        // Assert
        result.Should().NotBeNull();
        result!.Metadata.Should().ContainKey("key");

        var retrieved = await this._storage.GetConversationAsync(conversation.Id);
        retrieved!.Metadata.Should().ContainKey("key");
    }

    [Fact]
    public async Task UpdateConversationAsync_NonExistentConversation_ShouldReturnNullAsync()
    {
        // Arrange
        var conversation = CreateTestConversation(GetUniqueId("conv"));

        // Act
        var result = await this._storage!.UpdateConversationAsync(conversation);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteConversationAsync_ExistingConversation_ShouldDeleteConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation(GetUniqueId("conv"));
        await this._storage!.CreateConversationAsync(conversation);

        // Act
        var result = await this._storage.DeleteConversationAsync(conversation.Id);

        // Assert
        result.Should().BeTrue();

        var retrieved = await this._storage.GetConversationAsync(conversation.Id);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteConversationAsync_NonExistentConversation_ShouldReturnFalseAsync()
    {
        // Act
        var result = await this._storage!.DeleteConversationAsync("non-existent");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Item Tests

    [Fact]
    public async Task AddItemAsync_ShouldAddItemAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var msgId = GetUniqueId("msg");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        var message = CreateTestMessage(convId, msgId);

        // Act
        var result = await this._storage.AddItemAsync(convId, message);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(msgId);
    }

    [Fact]
    public async Task AddItemAsync_ToNonExistentConversation_ShouldThrowAsync()
    {
        // Arrange
        var convId = GetUniqueId("non-existent");
        var msgId = GetUniqueId("msg");
        var message = CreateTestMessage(convId, msgId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._storage!.AddItemAsync(convId, message));
    }

    [Fact]
    public async Task AddItemAsync_WithDuplicateId_ShouldThrowAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var msgId = GetUniqueId("msg");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        var message = CreateTestMessage(convId, msgId);
        await this._storage.AddItemAsync(convId, message);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._storage.AddItemAsync(convId, message));
    }

    [Fact]
    public async Task GetItemAsync_ExistingItem_ShouldReturnItemAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var msgId = GetUniqueId("msg");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        var message = CreateTestMessage(convId, msgId);
        await this._storage.AddItemAsync(convId, message);

        // Act
        var result = await this._storage.GetItemAsync(convId, msgId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(msgId);
    }

    [Fact]
    public async Task GetItemAsync_NonExistentItem_ShouldReturnNullAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        // Act
        var result = await this._storage.GetItemAsync(convId, "non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListItemsAsync_ShouldReturnItemsInDescendingOrderAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var msgId1 = GetUniqueId("msg");
        var msgId2 = GetUniqueId("msg");
        var msgId3 = GetUniqueId("msg");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        var msg1 = CreateTestMessage(convId, msgId1, createdAt: 1000);
        var msg2 = CreateTestMessage(convId, msgId2, createdAt: 2000);
        var msg3 = CreateTestMessage(convId, msgId3, createdAt: 3000);

        await this._storage.AddItemAsync(convId, msg1);
        await this._storage.AddItemAsync(convId, msg2);
        await this._storage.AddItemAsync(convId, msg3);

        // Act
        var result = await this._storage.ListItemsAsync(convId, limit: 10, order: SortOrder.Descending);

        // Assert
        result.Data.Should().HaveCount(3);
        result.Data[0].Id.Should().Be(msgId3);
        result.Data[1].Id.Should().Be(msgId2);
        result.Data[2].Id.Should().Be(msgId1);
    }

    [Fact]
    public async Task ListItemsAsync_ShouldReturnItemsInAscendingOrderAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var msgId1 = GetUniqueId("msg");
        var msgId2 = GetUniqueId("msg");
        var msgId3 = GetUniqueId("msg");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        var msg1 = CreateTestMessage(convId, msgId1, createdAt: 1000);
        var msg2 = CreateTestMessage(convId, msgId2, createdAt: 2000);
        var msg3 = CreateTestMessage(convId, msgId3, createdAt: 3000);

        await this._storage.AddItemAsync(convId, msg1);
        await this._storage.AddItemAsync(convId, msg2);
        await this._storage.AddItemAsync(convId, msg3);

        // Act
        var result = await this._storage.ListItemsAsync(convId, limit: 10, order: SortOrder.Ascending);

        // Assert
        result.Data.Should().HaveCount(3);
        result.Data[0].Id.Should().Be(msgId1);
        result.Data[1].Id.Should().Be(msgId2);
        result.Data[2].Id.Should().Be(msgId3);
    }

    [Fact]
    public async Task ListItemsAsync_WithLimit_ShouldReturnLimitedResultsAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        for (int i = 0; i < 5; i++)
        {
            var msgId = GetUniqueId($"msg-{i}");
            await this._storage.AddItemAsync(convId, CreateTestMessage(convId, msgId, createdAt: i));
        }

        // Act
        var result = await this._storage.ListItemsAsync(convId, limit: 3);

        // Assert
        result.Data.Should().HaveCount(3);
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteItemAsync_ExistingItem_ShouldDeleteItemAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var msgId = GetUniqueId("msg");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        var message = CreateTestMessage(convId, msgId);
        await this._storage.AddItemAsync(convId, message);

        // Act
        var result = await this._storage.DeleteItemAsync(convId, msgId);

        // Assert
        result.Should().BeTrue();

        var retrieved = await this._storage.GetItemAsync(convId, msgId);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteItemAsync_NonExistentItem_ShouldReturnFalseAsync()
    {
        // Arrange
        var convId = GetUniqueId("conv");
        var conversation = CreateTestConversation(convId);
        await this._storage!.CreateConversationAsync(conversation);

        // Act
        var result = await this._storage.DeleteItemAsync(convId, "non-existent");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task MultipleConversations_WithMessages_ShouldWorkCorrectlyAsync()
    {
        // Arrange & Act
        var convId1 = GetUniqueId("conv");
        var convId2 = GetUniqueId("conv");
        var msgId1_1 = GetUniqueId("msg");
        var msgId1_2 = GetUniqueId("msg");
        var msgId2_1 = GetUniqueId("msg");

        var conv1 = CreateTestConversation(convId1, createdAt: 1000);
        var conv2 = CreateTestConversation(convId2, createdAt: 2000);

        await this._storage!.CreateConversationAsync(conv1);
        await this._storage.CreateConversationAsync(conv2);

        await this._storage.AddItemAsync(convId1, CreateTestMessage(convId1, msgId1_1));
        await this._storage.AddItemAsync(convId1, CreateTestMessage(convId1, msgId1_2));
        await this._storage.AddItemAsync(convId2, CreateTestMessage(convId2, msgId2_1));

        // Assert conversations exist
        var conv1Retrieved = await this._storage.GetConversationAsync(convId1);
        var conv2Retrieved = await this._storage.GetConversationAsync(convId2);
        conv1Retrieved.Should().NotBeNull();
        conv2Retrieved.Should().NotBeNull();

        // Assert messages for conv-1
        var conv1Messages = await this._storage.ListItemsAsync(convId1);
        conv1Messages.Data.Should().HaveCount(2);

        // Assert messages for conv-2
        var conv2Messages = await this._storage.ListItemsAsync(convId2);
        conv2Messages.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteConversation_ShouldNotAffectOtherConversationsAsync()
    {
        // Arrange
        var convId1 = GetUniqueId("conv");
        var convId2 = GetUniqueId("conv");
        var msgId1 = GetUniqueId("msg");
        var msgId2 = GetUniqueId("msg");

        var conv1 = CreateTestConversation(convId1);
        var conv2 = CreateTestConversation(convId2);

        await this._storage!.CreateConversationAsync(conv1);
        await this._storage.CreateConversationAsync(conv2);

        await this._storage.AddItemAsync(convId1, CreateTestMessage(convId1, msgId1));
        await this._storage.AddItemAsync(convId2, CreateTestMessage(convId2, msgId2));

        // Act
        await this._storage.DeleteConversationAsync(convId1);

        // Assert
        var conv1Retrieved = await this._storage.GetConversationAsync(convId1);
        conv1Retrieved.Should().BeNull();

        var conv2Retrieved = await this._storage.GetConversationAsync(convId2);
        conv2Retrieved.Should().NotBeNull();

        var conv2Messages = await this._storage.ListItemsAsync(convId2);
        conv2Messages.Data.Should().HaveCount(1);
    }

    #endregion

    #region Helper Methods

    private static Conversation CreateTestConversation(string id, long createdAt = 1000)
    {
        return new Conversation
        {
            Id = id,
            CreatedAt = createdAt,
            Metadata = new Dictionary<string, string>()
        };
    }

    private static ResponsesUserMessageItemResource CreateTestMessage(string conversationId, string id, long createdAt = 1000)
    {
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
