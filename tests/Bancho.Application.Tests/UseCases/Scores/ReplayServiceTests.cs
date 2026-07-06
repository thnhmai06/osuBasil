using Bancho.Application.Abstractions;
using Bancho.Application.UseCases.Scores;
using Bancho.Domain;
using NSubstitute;

namespace Bancho.Application.Tests.UseCases.Scores;

public class ReplayServiceTests
{
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly IReplayStorage _replayStorage = Substitute.For<IReplayStorage>();
    private readonly IStatsRepository _stats = Substitute.For<IStatsRepository>();
    private readonly ReplayService _service;

    public ReplayServiceTests()
    {
        _service = new ReplayService(_scores, _replayStorage, _stats);
    }

    [Fact]
    public async Task FetchReplayFile_ScoreNotFound_ReturnsNotFound()
    {
        _scores.FetchOwnerAsync(1, Arg.Any<CancellationToken>()).Returns((ScoreOwnerRow?)null);

        var result = await _service.FetchReplayFileAsync(1, viewerId: 5);

        Assert.Equal(ReplayFetchResultCode.NotFound, result.Code);
    }

    [Fact]
    public async Task FetchReplayFile_FileMissingOnDisk_ReturnsNotFound()
    {
        _scores.FetchOwnerAsync(1, Arg.Any<CancellationToken>()).Returns(new ScoreOwnerRow(10, GameMode.VanillaOsu));
        _replayStorage.ReadAsync(1, Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        var result = await _service.FetchReplayFileAsync(1, viewerId: 5);

        Assert.Equal(ReplayFetchResultCode.NotFound, result.Code);
    }

    [Fact]
    public async Task FetchReplayFile_ViewerIsOwner_DoesNotIncrementViews()
    {
        _scores.FetchOwnerAsync(1, Arg.Any<CancellationToken>()).Returns(new ScoreOwnerRow(10, GameMode.VanillaOsu));
        _replayStorage.ReadAsync(1, Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        var result = await _service.FetchReplayFileAsync(1, viewerId: 10);

        Assert.Equal(ReplayFetchResultCode.Found, result.Code);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Data);
        await _stats.DidNotReceive().IncrementReplayViewsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchReplayFile_ViewerIsNotOwner_IncrementsOwnersViewCount()
    {
        _scores.FetchOwnerAsync(1, Arg.Any<CancellationToken>()).Returns(new ScoreOwnerRow(10, GameMode.VanillaTaiko));
        _replayStorage.ReadAsync(1, Arg.Any<CancellationToken>()).Returns([9]);

        await _service.FetchReplayFileAsync(1, viewerId: 999);

        await _stats.Received(1).IncrementReplayViewsAsync(10, (int)GameMode.VanillaTaiko, Arg.Any<CancellationToken>());
    }
}
