using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;
using Moq;

namespace ClaudeCodeProxy.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestsService"/> using a mocked <see cref="IRecordingRepository"/>
/// to verify page-size clamping and skip calculation without hitting the database.
/// </summary>
[TestFixture]
public class RequestsServiceTests
{
    private Mock<IRecordingRepository> _repositoryMock = null!;
    private RequestsService _sut = null!;

    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To   = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IRecordingRepository>();
        _repositoryMock
            .Setup(r => r.GetLlmRequestsAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LlmRequestSummary>());

        _sut = new RequestsService(_repositoryMock.Object);
    }

    [Test]
    public async Task GetRecentLlmRequestsAsync_ClampsPageSizeAbove200()
    {
        await _sut.GetRecentLlmRequestsAsync(From, To, page: 0, pageSize: 500);

        _repositoryMock.Verify(r => r.GetLlmRequestsAsync(
            From, To,
            0,    // skip
            200,  // clamped from 500
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetRecentLlmRequestsAsync_ClampsPageSizeBelow1()
    {
        await _sut.GetRecentLlmRequestsAsync(From, To, page: 0, pageSize: 0);

        _repositoryMock.Verify(r => r.GetLlmRequestsAsync(
            From, To,
            0,  // skip
            1,  // clamped from 0
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetRecentLlmRequestsAsync_CalculatesSkipFromPageAndPageSize()
    {
        await _sut.GetRecentLlmRequestsAsync(From, To, page: 3, pageSize: 10);

        _repositoryMock.Verify(r => r.GetLlmRequestsAsync(
            From, To,
            30,  // skip = page * pageSize = 3 * 10
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetRecentLlmRequestsAsync_ValidPageSize_PassesThroughUnchanged()
    {
        await _sut.GetRecentLlmRequestsAsync(From, To, page: 1, pageSize: 50);

        _repositoryMock.Verify(r => r.GetLlmRequestsAsync(
            From, To,
            50,  // skip = 1 * 50
            50,  // unchanged
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetLlmRequestDetailAsync_DelegatesDirectlyToRepository()
    {
        var detail = new LlmRequestDetail { Id = 42, Method = "POST", Path = "/v1/messages" };
        _repositoryMock
            .Setup(r => r.GetLlmRequestByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _sut.GetLlmRequestDetailAsync(42);

        Assert.That(result, Is.SameAs(detail));
    }

    [Test]
    public async Task GetLlmRequestDetailAsync_ReturnsNull_WhenRepositoryReturnsNull()
    {
        _repositoryMock
            .Setup(r => r.GetLlmRequestByIdAsync(99999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmRequestDetail?)null);

        var result = await _sut.GetLlmRequestDetailAsync(99999);

        Assert.That(result, Is.Null);
    }
}
