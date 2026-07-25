using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using NSubstitute;

namespace Basil.Application.Tests.Services.Multiplayer;

/// <summary>Covers <see cref="MatchLiveSnapshotBuilder.BuildRoomLive" /> — new for the Phase 2 record redesign, replacing the old flat `IsOpen` bool on `GET /matches` list items with this room-core-plus-map embed.</summary>
public class MatchLiveSnapshotBuilderTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();

    private static MatchSession MakeMatch(string mapMd5 = "")
    {
        return new MatchSession(0, "Grand Finals", "hunter2", "map", 42, mapMd5, 1, GameMode.Standard,
            Mods.NoMod, MatchWinCondition.Score, MatchTeamType.TeamVs, false, 0, "#mp_0") { DbId = 5 };
    }

    [Fact]
    public async Task BuildRoomLive_NoMapAssigned_BeatmapNullAndNeverQueriesRepository()
    {
        var match = MakeMatch();

        var live = await MatchLiveSnapshotBuilder.BuildRoomLive(match, _maps);

        Assert.Null(live.Beatmap);
        await _maps.DidNotReceive().FetchOneAsync(
            Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildRoomLive_ReflectsRoomCoreFieldsAndHasPassword()
    {
        var match = MakeMatch();

        var live = await MatchLiveSnapshotBuilder.BuildRoomLive(match, _maps);

        Assert.Equal(5, live.Id);
        Assert.Equal("Grand Finals", live.Name);
        Assert.True(live.HasPassword);
        Assert.Equal(42, live.MapId);
        Assert.Equal(MatchTeamType.TeamVs, live.TeamType);
        Assert.False(live.InProgress);
    }

    [Fact]
    public async Task BuildRoomLive_UnresolvableMapMd5_BeatmapNull()
    {
        var match = MakeMatch("d41d8cd98f00b204e9800998ecf8427e");
        _maps.FetchOneAsync(md5: match.MapMd5, includePrivate: true, cancellationToken: Arg.Any<CancellationToken>())
            .Returns((Beatmap?)null);

        var live = await MatchLiveSnapshotBuilder.BuildRoomLive(match, _maps);

        Assert.Null(live.Beatmap);
    }
}
