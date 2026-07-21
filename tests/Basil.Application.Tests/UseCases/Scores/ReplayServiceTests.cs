using Basil.Application.Abstractions.Scores;
using Basil.Application.Services.Scores;
using Basil.Domain.Beatmaps;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Scores;

public class ReplayServiceTests
{
    private readonly IReplayStorage _replayStorage = Substitute.For<IReplayStorage>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly ReplayService _service;

    public ReplayServiceTests()
    {
        _service = new ReplayService(_scores, _replayStorage);
    }

    [Fact]
    public async Task FetchReplayFile_ScoreNotFound_ReturnsNotFound()
    {
        _scores.FetchOwnerAsync(1, Arg.Any<CancellationToken>()).Returns((ScoreOwnerRow?)null);

        var result = await _service.FetchReplayFileAsync(1);

        Assert.Equal(ReplayFetchResultCode.NotFound, result.Code);
    }

    [Fact]
    public async Task FetchReplayFile_FileMissingOnDisk_ReturnsNotFound()
    {
        _scores.FetchOwnerAsync(1, Arg.Any<CancellationToken>()).Returns(new ScoreOwnerRow(10, GameMode.Standard));
        _replayStorage.ReadAsync(1, Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        var result = await _service.FetchReplayFileAsync(1);

        Assert.Equal(ReplayFetchResultCode.NotFound, result.Code);
    }

    [Fact]
    public async Task FetchReplayFile_Found_ReturnsData()
    {
        _scores.FetchOwnerAsync(1, Arg.Any<CancellationToken>()).Returns(new ScoreOwnerRow(10, GameMode.Standard));
        _replayStorage.ReadAsync(1, Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        var result = await _service.FetchReplayFileAsync(1);

        Assert.Equal(ReplayFetchResultCode.Found, result.Code);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Data);
    }
}
