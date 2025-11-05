// Copyright (c) Microsoft. All rights reserved.

using System.Net;
using System.Text.Json;
using AgentGateway.Models;
using AgentGateway.Responses.Models;
using AgentWebChat.AgentHost.DurableAgents.Utilities;
using Microsoft.Extensions.AI;
using Moq;
using Moq.Protected;

namespace AgentWebChat.AgentHost.DurableAgents.Tests;

/// <summary>
/// Tests for ConversationsChatMessageStore to verify pagination support.
/// </summary>
public sealed class ConversationsChatMessageStoreTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ConversationsChatMessageStoreTests()
    {
        this._jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    [Fact]
    public async Task GetMessagesAsync_WithSinglePage_ReturnsAllMessagesAsync()
    {
        // Arrange
        HttpClient httpClient = this.CreateMockHttpClient((request, callCount) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("/items") == true)
            {
                ListResponse<ItemResource> response = new()
                {
                    Data =
                    [
                        this.CreateMessageItem("msg_1", "user", "Hello"),
                        this.CreateMessageItem("msg_2", "assistant", "Hi there!")
                    ],
                    HasMore = false,
                    FirstId = "msg_1",
                    LastId = "msg_2"
                };
                return this.CreateJsonResponse(response);
            }

            throw new InvalidOperationException("Unexpected request");
        });

        ConversationsApiClient apiClient = new(httpClient, this._jsonOptions);
        ConversationsChatMessageStore store = new(apiClient, "conv_123");

        // Act
        IEnumerable<ChatMessage> messages = await store.GetMessagesAsync();

        // Assert
        List<ChatMessage> messageList = messages.ToList();
        messageList.Should().HaveCount(2);
        messageList[0].Role.Should().Be(ChatRole.User);
        messageList[0].Text.Should().Be("Hello");
        messageList[1].Role.Should().Be(ChatRole.Assistant);
        messageList[1].Text.Should().Be("Hi there!");
    }

    [Fact]
    public async Task GetMessagesAsync_WithMultiplePages_ReturnsAllMessagesAsync()
    {
        // Arrange
        int callCount = 0;
        HttpClient httpClient = this.CreateMockHttpClient((request, _) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("/items") == true)
            {
                string query = request.RequestUri.Query;
                callCount++;

                if (callCount == 1)
                {
                    // First page
                    query.Should().NotContain("after=");
                    ListResponse<ItemResource> response = new()
                    {
                        Data =
                        [
                            this.CreateMessageItem("msg_1", "user", "Message 1"),
                            this.CreateMessageItem("msg_2", "assistant", "Message 2"),
                            this.CreateMessageItem("msg_3", "user", "Message 3")
                        ],
                        HasMore = true,
                        FirstId = "msg_1",
                        LastId = "msg_3"
                    };
                    return this.CreateJsonResponse(response);
                }
                else if (callCount == 2)
                {
                    // Second page
                    query.Should().Contain("after=msg_3");
                    ListResponse<ItemResource> response = new()
                    {
                        Data =
                        [
                            this.CreateMessageItem("msg_4", "assistant", "Message 4"),
                            this.CreateMessageItem("msg_5", "user", "Message 5")
                        ],
                        HasMore = true,
                        FirstId = "msg_4",
                        LastId = "msg_5"
                    };
                    return this.CreateJsonResponse(response);
                }
                else if (callCount == 3)
                {
                    // Third page
                    query.Should().Contain("after=msg_5");
                    ListResponse<ItemResource> response = new()
                    {
                        Data =
                        [
                            this.CreateMessageItem("msg_6", "assistant", "Message 6")
                        ],
                        HasMore = false,
                        FirstId = "msg_6",
                        LastId = "msg_6"
                    };
                    return this.CreateJsonResponse(response);
                }
            }

            throw new InvalidOperationException("Unexpected request");
        });

        ConversationsApiClient apiClient = new(httpClient, this._jsonOptions);
        ConversationsChatMessageStore store = new(apiClient, "conv_123");

        // Act
        IEnumerable<ChatMessage> messages = await store.GetMessagesAsync();

        // Assert
        List<ChatMessage> messageList = messages.ToList();
        messageList.Should().HaveCount(6);
        messageList[0].Text.Should().Be("Message 1");
        messageList[1].Text.Should().Be("Message 2");
        messageList[2].Text.Should().Be("Message 3");
        messageList[3].Text.Should().Be("Message 4");
        messageList[4].Text.Should().Be("Message 5");
        messageList[5].Text.Should().Be("Message 6");
        callCount.Should().Be(3, "should make 3 API calls to retrieve all pages");
    }

    [Fact]
    public async Task GetMessagesAsync_WithEmptyConversation_ReturnsEmptyListAsync()
    {
        // Arrange
        HttpClient httpClient = this.CreateMockHttpClient((request, callCount) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("/items") == true)
            {
                ListResponse<ItemResource> response = new()
                {
                    Data = [],
                    HasMore = false,
                    FirstId = null,
                    LastId = null
                };
                return this.CreateJsonResponse(response);
            }

            throw new InvalidOperationException("Unexpected request");
        });

        ConversationsApiClient apiClient = new(httpClient, this._jsonOptions);
        ConversationsChatMessageStore store = new(apiClient, "conv_123");

        // Act
        IEnumerable<ChatMessage> messages = await store.GetMessagesAsync();

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMessagesAsync_WithNonMessageItems_FiltersThemOutAsync()
    {
        // Arrange
        HttpClient httpClient = this.CreateMockHttpClient((request, callCount) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("/items") == true)
            {
                ListResponse<ItemResource> response = new()
                {
                    Data =
                    [
                        this.CreateMessageItem("msg_1", "user", "Hello"),
                        this.CreateFunctionCallItem("fc_1"),
                        this.CreateMessageItem("msg_2", "assistant", "Hi there!"),
                        this.CreateFunctionCallResultItem("fcr_1")
                    ],
                    HasMore = false,
                    FirstId = "msg_1",
                    LastId = "fcr_1"
                };
                return this.CreateJsonResponse(response);
            }

            throw new InvalidOperationException("Unexpected request");
        });

        ConversationsApiClient apiClient = new(httpClient, this._jsonOptions);
        ConversationsChatMessageStore store = new(apiClient, "conv_123");

        // Act
        IEnumerable<ChatMessage> messages = await store.GetMessagesAsync();

        // Assert
        List<ChatMessage> messageList = messages.ToList();
        messageList.Should().HaveCount(2, "only message items should be included");
        messageList[0].Text.Should().Be("Hello");
        messageList[1].Text.Should().Be("Hi there!");
    }

    [Fact]
    public async Task ConversationsApiClient_ListItemsAsync_IncludesAfterParameterAsync()
    {
        // Arrange
        HttpClient httpClient = this.CreateMockHttpClient((request, callCount) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("/items") == true)
            {
                string query = request.RequestUri.Query;
                query.Should().Contain("after=msg_5");
                query.Should().Contain("order=asc");
                query.Should().Contain("limit=100");

                ListResponse<ItemResource> response = new()
                {
                    Data =
                    [
                        this.CreateMessageItem("msg_6", "user", "Message 6")
                    ],
                    HasMore = false,
                    FirstId = "msg_6",
                    LastId = "msg_6"
                };
                return this.CreateJsonResponse(response);
            }

            throw new InvalidOperationException("Unexpected request");
        });

        ConversationsApiClient apiClient = new(httpClient, this._jsonOptions);

        // Act
        ListResponse<ItemResource> result = await apiClient.ListItemsAsync("conv_123", "asc", 100, "msg_5");

        // Assert
        result.Data.Should().HaveCount(1);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task ConversationsApiClient_ListItemsAsync_WithoutAfter_OmitsParameterAsync()
    {
        // Arrange
        HttpClient httpClient = this.CreateMockHttpClient((request, callCount) =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("/items") == true)
            {
                string query = request.RequestUri.Query;
                query.Should().NotContain("after=");
                query.Should().Contain("order=asc");
                query.Should().Contain("limit=100");

                ListResponse<ItemResource> response = new()
                {
                    Data =
                    [
                        this.CreateMessageItem("msg_1", "user", "Message 1")
                    ],
                    HasMore = false,
                    FirstId = "msg_1",
                    LastId = "msg_1"
                };
                return this.CreateJsonResponse(response);
            }

            throw new InvalidOperationException("Unexpected request");
        });

        ConversationsApiClient apiClient = new(httpClient, this._jsonOptions);

        // Act
        ListResponse<ItemResource> result = await apiClient.ListItemsAsync("conv_123", "asc", 100);

        // Assert
        result.Data.Should().HaveCount(1);
    }

    private HttpClient CreateMockHttpClient(Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory)
    {
        int callCount = 0;
        Mock<HttpMessageHandler> mockHandler = new();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                callCount++;
                return responseFactory(request, callCount);
            });

        return new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:5000")
        };
    }

    private HttpResponseMessage CreateJsonResponse<T>(T data)
    {
        string json = JsonSerializer.Serialize(data, this._jsonOptions);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private ItemResource CreateMessageItem(string id, string role, string text)
    {
        // Create the appropriate message type based on role
        return role.ToUpperInvariant() switch
        {
            "USER" => new ResponsesUserMessageItemResource
            {
                Id = id,
                Content =
                [
                    new ItemContentInputText
                    {
                        Text = text
                    }
                ],
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            "ASSISTANT" => new ResponsesAssistantMessageItemResource
            {
                Id = id,
                Content =
                [
                    new ItemContentOutputText
                    {
                        Text = text,
                        Annotations = []
                    }
                ],
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            "SYSTEM" => new ResponsesSystemMessageItemResource
            {
                Id = id,
                Content =
                [
                    new ItemContentInputText
                    {
                        Text = text
                    }
                ],
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            _ => throw new ArgumentException($"Unsupported role: {role}", nameof(role))
        };
    }

    private FunctionToolCallItemResource CreateFunctionCallItem(string id)
    {
        // Create a function call item (which is not a ResponsesMessageItemResource)
        return new FunctionToolCallItemResource
        {
            Id = id,
            CallId = $"call_{id}",
            Name = "test_function",
            Arguments = "{}",
            Status = FunctionToolCallItemResourceStatus.Completed
        };
    }

    private FunctionToolCallOutputItemResource CreateFunctionCallResultItem(string id)
    {
        // Create a function call result item (which is not a ResponsesMessageItemResource)
        return new FunctionToolCallOutputItemResource
        {
            Id = id,
            CallId = $"call_{id}",
            Output = "test output"
        };
    }
}
