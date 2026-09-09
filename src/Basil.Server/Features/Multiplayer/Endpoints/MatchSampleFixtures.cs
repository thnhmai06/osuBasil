using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Server.Features.Beatmaps;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>
///     Example payloads shared by the OpenAPI documentation of several `/matches` routes, kept in one
///     place because the same sample beatmap and settings back multiple endpoints' documented
///     examples.
/// </summary>
internal static class MatchSampleFixtures
{
	public static MatchSettingsView SampleSettings()
	{
		return new MatchSettingsView(42, "Grand Finals: Alpha vs Bravo", true, false, false, 16, 654,
			Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard,
			new UserBrief(7, "Alice", Country.Us),
			[new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)],
			SampleBeatmap());
	}

	public static MatchRoomLive SampleRoomLive()
	{
		return new MatchRoomLive(true, false, false, 16, 654,
			Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard,
			true, SampleBeatmap());
	}

	internal static MatchLiveSnapshot SampleLiveSnapshot()
	{
		var slots = new List<MatchSlotView>(16);
		for (var i = 0; i < 16; i++)
			slots.Add(new MatchSlotView(i + 1, null, SlotStatus.Open, null, null, null, null));
		slots[0] = new MatchSlotView(1, new UserBrief(7, "Alice", Country.Us), SlotStatus.Playing, MatchTeam.Red,
			Mods.NoMod, true, true);

		return new MatchLiveSnapshot(42, "Grand Finals: Alpha vs Bravo", true, false, false, 16, 654,
			Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard, true,
			new UserBrief(7, "Alice", Country.Us),
			[new UserBrief(8, "Bob", Country.Gb), new UserBrief(13, "Erin", Country.Ie)],
			SampleBeatmap(), slots);
	}

	public static BeatmapDetail SampleBeatmap()
	{
		var created = DateTime.Parse("2026-06-01T10:00:00Z");
		var beatmapset = new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created,
			created, false, false, BeatmapStatus.Loved, 1);
		var difficulty = new Difficulty(GameMode.Standard, 174, TimeSpan.FromSeconds(225), 4, 9, 8, 6, 6.42);
		var objectCounts = new OsuBeatmapObjectCounts
			{ Total = 832, MaxCombo = 1234, Circles = 620, Sliders = 210, Spinners = 2 };
		return new BeatmapDetail("d41d8cd98f00b204e9800998ecf8427e", 654, "Extreme",
			difficulty, objectCounts, false, beatmapset);
	}

	public static MatchReport SampleMatchReport()
	{
		var created = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
		var started = DateTimeOffset.Parse("2026-07-20T12:00:00Z");
		var ended = DateTimeOffset.Parse("2026-07-20T12:04:30Z");

		var beatmapset = new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created,
			created, false, false, BeatmapStatus.Loved, 1);
		var difficulty = new Difficulty(GameMode.Standard, 174, TimeSpan.FromSeconds(225), 4, 9, 8, 6, 6.42);
		var objectCounts = new OsuBeatmapObjectCounts
			{ Total = 832, MaxCombo = 1234, Circles = 620, Sliders = 210, Spinners = 2 };
		var beatmap = new BeatmapDetail("d41d8cd98f00b204e9800998ecf8427e", 654, "Extreme",
			difficulty, objectCounts, false, beatmapset);

		var live = new MatchRoomLive(true, false, false, 16, 654,
			Mods.NoMod, false, MatchTeamType.TeamVs, MatchWinCondition.ScoreV2, GameMode.Standard, false, beatmap);

		var score = new MatchReportScore(new UserBrief(7, "Alice", Country.Vn), MatchTeam.Red, Mods.NoMod, 4_850_213,
			98.42, 1234, 720, 45, 3, 2, 12, 5, Grade.A, false, ended);
		var round = new MatchReportRound(0, "d41d8cd98f00b204e9800998ecf8427e", beatmap,
			GameMode.Standard, MatchWinCondition.ScoreV2, MatchTeamType.TeamVs, Mods.NoMod, false, started, ended,
			new UserBrief(7, "Alice", Country.Vn), MatchTeam.Red, MatchWinCondition.Score, 1_200_000, [score]);
		var evt = new MatchReportEvent(MatchEventType.Created, new UserBrief(7, "Alice", Country.Vn), null, started,
			null);

		return new MatchReport(42, "Grand Finals: Alpha vs Bravo", started, null, live, [evt], [round]);
	}
}