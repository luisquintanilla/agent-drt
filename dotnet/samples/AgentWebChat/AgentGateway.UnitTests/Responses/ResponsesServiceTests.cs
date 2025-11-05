// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Models;
using AgentGateway.Responses;
using AgentGateway.Responses.Models;
using Moq;

namespace AgentGateway.UnitTests.Responses;

/// <summary>
/// Tests for OrleansResponsesService.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public class ResponsesServiceTests
{
    private readonly OrleansTestClusterFixture _fixture;
    private readonly OrleansResponsesService _service;

    public ResponsesServiceTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;
        this._service = new OrleansResponsesService(fixture.GrainFactory);

        // Reset the mock before each test to ensure test isolation
        this._fixture.ResetResponseExecutorMock();

        // Setup default mock response
        this._fixture.SetupDefaultResponseExecutor();
    }

    private static CreateResponse CreateTestRequest() => new()
    {
        Input = ResponseInput.FromText("Hello, how are you?"),
        Model = "gpt-4",
        Instructions = "You are a helpful assistant."
    };

    [Fact]
    public void Constructor_WithValidGrainFactory_Succeeds()
    {
        // Act
        IResponsesService service = new OrleansResponsesService(this._fixture.GrainFactory);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullGrainFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        FluentActions.Invoking(() => new OrleansResponsesService(null!))
            .Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("grainFactory");
    }

    [Fact]
    public async Task CreateResponseAsync_ShouldCreateResponseAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();

        // Act
        Response result = await this._service.CreateResponseAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().StartWith("resp_");
        result.Status.Should().BeDefined();
    }

    [Fact]
    public async Task CreateResponseAsync_MultipleTimes_CreatesUniqueResponsesAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();

        // Act
        Response result1 = await this._service.CreateResponseAsync(request, CancellationToken.None);
        Response result2 = await this._service.CreateResponseAsync(request, CancellationToken.None);

        // Assert
        result1.Id.Should().NotBe(result2.Id);
    }

    [Fact]
    public async Task GetResponseAsync_WhenExists_ShouldReturnResponseAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();
        Response created = await this._service.CreateResponseAsync(request, CancellationToken.None);

        // Act
        Response? result = await this._service.GetResponseAsync(created.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetResponseAsync_WhenDoesNotExist_ShouldReturnNullAsync()
    {
        // Act
        Response? result = await this._service.GetResponseAsync("resp_nonexistent", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListResponseInputItemsAsync_ShouldReturnItemsAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();
        Response created = await this._service.CreateResponseAsync(request, CancellationToken.None);

        // Act
        ListResponse<ItemResource> result = await this._service.ListResponseInputItemsAsync(
            created.Id,
            10,
            SortOrder.Ascending,
            null,
            null,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateResponseStreamingAsync_ShouldReturnStreamAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();

        // Act
        IAsyncEnumerable<StreamingResponseEvent> stream = this._service.CreateResponseStreamingAsync(request, CancellationToken.None);

        // Assert
        stream.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateResponseStreamingAsync_ShouldEmitProperEventSequenceAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();

        // Set up custom mock with output items for this test
        this._fixture.ResponseExecutorMock
            .Setup(x => x.ExecuteAsync(
                It.IsAny<AgentInvocationContext>(),
                It.IsAny<CreateResponse>(),
                It.IsAny<CancellationToken>()))
            .Returns((AgentInvocationContext ctx,
                     CreateResponse req,
                     CancellationToken ct) => GetTestStreamingResponseWithOutputAsync(ctx, req));

        // Act
        var events = new List<StreamingResponseEvent>();
        await foreach (var evt in this._service.CreateResponseStreamingAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().NotBeEmpty();

        // First event should be response.created
        events[0].Should().BeOfType<StreamingResponseCreated>();
        events[0].Type.Should().Be("response.created");

        // Last event should be response.completed
        events[^1].Should().BeOfType<StreamingResponseCompleted>();
        events[^1].Type.Should().Be("response.completed");

        // Verify the completed response has output
        var completedEvent = (StreamingResponseCompleted)events[^1];
        completedEvent.Response.Status.Should().Be(ResponseStatus.Completed);
        completedEvent.Response.Output.Should().NotBeEmpty();

        // All events should have sequence numbers assigned by the grain
        events.Should().OnlyContain(e => e.SequenceNumber > 0);
    }

    private static async IAsyncEnumerable<StreamingResponseEvent> GetTestStreamingResponseWithOutputAsync(
        AgentInvocationContext context,
        CreateResponse request)
    {
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var itemId = context.IdGenerator.GenerateMessageId();
        var sequenceNumber = 0;

        // Emit response.created event
        yield return new StreamingResponseCreated
        {
            SequenceNumber = ++sequenceNumber,
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
                    InputTokensDetails = new InputTokensDetails { CachedTokens = 0 },
                    OutputTokensDetails = new OutputTokensDetails { ReasoningTokens = 0 }
                },
                Tools = []
            }
        };

        // Emit output.item.added event
        yield return new StreamingOutputItemAdded
        {
            SequenceNumber = ++sequenceNumber,
            Item = new ResponsesAssistantMessageItemResource
            {
                Id = itemId,
                Content = []
            }
        };

        // Emit content.part.added event
        yield return new StreamingContentPartAdded
        {
            SequenceNumber = ++sequenceNumber,
            ItemId = itemId,
            ContentIndex = 0,
            Part = new ItemContentOutputText { Text = string.Empty, Annotations = [] }
        };

        // Emit output.text.delta event
        yield return new StreamingOutputTextDelta
        {
            SequenceNumber = ++sequenceNumber,
            ItemId = itemId,
            ContentIndex = 0,
            Delta = "Test response"
        };

        // Emit output.text.done event
        yield return new StreamingOutputTextDone
        {
            SequenceNumber = ++sequenceNumber,
            ItemId = itemId,
            ContentIndex = 0,
            Text = "Test response"
        };

        // Emit content.part.done event
        yield return new StreamingContentPartDone
        {
            SequenceNumber = ++sequenceNumber,
            ItemId = itemId,
            ContentIndex = 0,
            Part = new ItemContentOutputText { Text = "Test response", Annotations = [] }
        };

        // Emit output.item.done event
        yield return new StreamingOutputItemDone
        {
            SequenceNumber = ++sequenceNumber,
            Item = new ResponsesAssistantMessageItemResource
            {
                Id = itemId,
                Content = [new ItemContentOutputText { Text = "Test response", Annotations = [] }]
            }
        };

        // Emit response.completed event
        yield return new StreamingResponseCompleted
        {
            SequenceNumber = ++sequenceNumber,
            Response = new Response
            {
                Id = context.ResponseId,
                Status = ResponseStatus.Completed,
                CreatedAt = createdAt,
                Model = request.Model,
                Instructions = request.Instructions,
                Output = [new ResponsesAssistantMessageItemResource
                {
                    Id = itemId,
                    Content = [new ItemContentOutputText { Text = "Test response", Annotations = [] }]
                }],
                Usage = new ResponseUsage
                {
                    InputTokens = 5,
                    OutputTokens = 10,
                    TotalTokens = 15,
                    InputTokensDetails = new InputTokensDetails { CachedTokens = 0 },
                    OutputTokensDetails = new OutputTokensDetails { ReasoningTokens = 0 }
                },
                Tools = []
            }
        };

        await Task.CompletedTask;
    }
}
