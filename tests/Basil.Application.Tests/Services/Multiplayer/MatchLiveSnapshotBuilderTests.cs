using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using NSubstitute;

namespace Basil.Application.Tests.Services.Multiplayer;

/// <summary>
///     Covers <see cref="MatchLiveSnapshotBuilder.BuildRoomLive" />'s room-core-plus-map embed.
/// </summary>
public class MatchLiveSnapshotBuilderTests
{
	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();

	private static MatchSession MakeMatch(string mapMd5 = "", int? mapId = 42)
	{
		return new MatchSession(0, "Grand Finals", "hunter2", "map", mapId, mapMd5, 1, GameMode.Standard,
				Mods.NoMod, MatchWinCondition.Score, MatchTeamType.TeamVs, false, 0, "#mp_0")
			{ DbId = 5 };
	}

	[Fact]
	public async Task BuildRoomLive_NoMapAssigned_BeatmapNullAndNeverQueriesRepository()
	{
		var match = MakeMatch();

		var live = await MatchLiveSnapshotBuilder.BuildRoomLive(match, _beatmaps);

		Assert.Null(live.Beatmap);
		await _beatmaps.DidNotReceive().FetchOneAsync(
			Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task BuildRoomLive_ReflectsRoomFieldsAndHasPassword()
	{
		var match = MakeMatch();

		var live = await MatchLiveSnapshotBuilder.BuildRoomLive(match, _beatmaps);

		// Id/Name are deliberately absent from MatchRoomLive -- every place it's embedded already
		// carries those at its own top level (see the type's doc comment).
		Assert.True(live.HasPassword);
		Assert.Equal(42, live.MapId);
		Assert.Equal(MatchTeamType.TeamVs, live.TeamType);
		Assert.False(live.InProgress);
	}

	/// <summary>
	///     Regression test (Issue #4): `mapId` used to keep the osu! protocol's `-1` sentinel on the
	///     wire even in JSON API responses. It is now `null` in the same case `Beatmap` is, matching
	///     <see cref="MatchSession.MapId" />'s own domain representation.
	/// </summary>
	[Fact]
	public async Task BuildRoomLive_NoMapChosen_MapIdIsNull()
	{
		var match = MakeMatch(mapId: null);

		var live = await MatchLiveSnapshotBuilder.BuildRoomLive(match, _beatmaps);

		Assert.Null(live.MapId);
		Assert.Null(live.Beatmap);
	}

	[Fact]
	public async Task BuildRoomLive_UnresolvableMapMd5_BeatmapNull()
	{
		var match = MakeMatch("d41d8cd98f00b204e9800998ecf8427e");
		_beatmaps.FetchOneAsync(md5: match.MapMd5, includePrivate: true,
				cancellationToken: Arg.Any<CancellationToken>())
			.Returns((Beatmap?)null);

		var live = await MatchLiveSnapshotBuilder.BuildRoomLive(match, _beatmaps);

		Assert.Null(live.Beatmap);
	}
}