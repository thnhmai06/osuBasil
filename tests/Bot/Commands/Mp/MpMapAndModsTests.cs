using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Sessions;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;
using Basil.Testing;
using NSubstitute;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     The commands that choose the beatmap and the mods of a room: <c>!mp map</c> and <c>!mp mods</c>. Expected
///     replies are named by the reply that owns the wording.
/// </summary>
public sealed class MpMapAndModsTests
{
	/// <summary>The id of the beatmap the repository knows in these tests.</summary>
	private const int KnownBeatmapId = 1234;

	[Theory]
	[InlineData("!mp map")]
	[InlineData("!mp map x")]
	public async Task Map_WithoutANumericId_SaysHowToUseItAndSelectsNothing(string message)
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MapUsage)], said.Here);
		Assert.Null(room.Settings.Beatmap);
	}

	[Fact]
	public async Task Map_ForAnIdNoBeatmapHas_SaysNoBeatmapWasFoundAndSelectsNothing()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp map 999");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NoBeatmapWithId, new { beatmap = 999 })], said.Here);
		Assert.Null(room.Settings.Beatmap);
	}

	[Fact]
	public async Task Map_ForAKnownBeatmap_SaysItsArtistTitleAndVersionAndSelectsIt()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		var beatmap = KnowBeatmap(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", $"!mp map {KnownBeatmapId}");

		Assert.True(succeeded);
		Assert.Equal(
			[
				bot.TextIn(room.Id, MpReplies.ChangedBeatmap,
					new { artist = "Camellia", title = "Exit This Earth's Atomosphere", version = "Hyper" })
			],
			said.Here);
		Assert.Equal(KnownBeatmapId, room.Settings.Beatmap!.Id);
		Assert.Equal(beatmap.Hash, room.Settings.Beatmap.Hash);
	}

	[Fact]
	public async Task Mods_WithoutAnArgument_SaysHowToUseItAndChangesNothing()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp mods");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.ModsUsage)], said.Here);
		Assert.Equal(GameMods.NoMod, room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task Mods_WithHidden_SaysHiddenWasEnabledAndTheRoomHasIt()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp mods HD");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.EnabledMods, new { mods = "Hidden" })], said.Here);
		Assert.Equal(GameMods.Hidden, room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task Mods_AddingHardRockToHidden_SaysOnlyHardRockWasEnabled()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp mods HD")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp mods HD HR");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.EnabledMods, new { mods = "HardRock" })], said.Here);
		Assert.Equal(GameMods.Hidden | GameMods.HardRock, room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task Mods_WithNoneAfterHidden_SaysHiddenWasDisabledAndClearsTheMods()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp mods HD")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp mods None");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.DisabledMods, new { mods = "Hidden" })], said.Here);
		Assert.Equal(GameMods.NoMod, room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task Mods_WithTheSameModsAgain_SaysNothingChanged()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp mods HD")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp mods HD");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NoModChanges)], said.Here);
		Assert.Equal(GameMods.Hidden, room.Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task Mods_WithFreemod_SaysFreemodWasEnabledAndTheRoomIsFreemod()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp mods Freemod");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.EnabledFreemod)], said.Here);
		Assert.True(room.Settings.MatchSettings.Freemods);
	}

	[Fact]
	public async Task Mods_WithNoneAfterFreemod_SaysFreemodWasDisabledAndTheRoomIsNoLongerFreemod()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp mods Freemod")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp mods None");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.DisabledFreemod)], said.Here);
		Assert.False(room.Settings.MatchSettings.Freemods);
	}

	[Fact]
	public async Task Mods_WithACombinationTheModeDoesNotAllow_SaysTheModsAreInvalidAndKeepsTheModsTheRoomHad()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp mods HD")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp mods EZ HR");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.InvalidMods)], said.Here);
		Assert.Equal(GameMods.Hidden, room.Settings.MatchSettings.Mods);
	}

	[Theory]
	[InlineData("!mp map 1234")]
	[InlineData("!mp mods HD")]
	public async Task MapOrModsCommand_ByAPlainSeatedPlayer_IsRefusedAndChangesNothing(string message)
	{
		using var bot = new BotHarness();
		var (room, _, player) = await SetUpAsync(bot);
		KnowBeatmap(bot);

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.Null(room.Settings.Beatmap);
		Assert.Equal(GameMods.NoMod, room.Settings.MatchSettings.Mods);
	}

	/// <summary>
	///     Opens a room as "Creator" (seated in slot 1 as host, targeting the room) and seats "Player" in slot 2 with
	///     <c>!mp join</c>.
	/// </summary>
	private static async Task<(Room Room, BanchoConnection Creator, BanchoConnection Player)> SetUpAsync(BotHarness bot)
	{
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");
		Assert.True((await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}")).Succeeded);
		return (room, creator, player);
	}

	/// <summary>Makes the beatmap repository of the bot know a standard-mode beatmap under <see cref="KnownBeatmapId" />.</summary>
	private static BeatmapRecord KnowBeatmap(BotHarness bot)
	{
		var now = DateTimeOffset.UnixEpoch;
		var beatmap = new BeatmapRecord
		{
			Id = KnownBeatmapId,
			Beatmapset = new BeatmapsetRecord
			{
				Id = 100,
				Fingerprint = new ContentFingerprint(new ContentStamp(0, now),
					new Md5("00000000000000000000000000000000")),
				Metadata = new BeatmapsetMetadata
				{
					Artist = "Camellia",
					Title = "Exit This Earth's Atomosphere",
					Creator = "cmyui"
				},
				Assets = new BeatmapsetAssets(),
				UpdatedAt = now,
				CreatedAt = now
			},
			Hash = new Md5("d41d8cd98f00b204e9800998ecf8427e"),
			Metadata = new BeatmapMetadata
			{
				Version = "Hyper",
				Difficulty = new BeatmapDifficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0,
					5.0, 6.5),
				Objects = new OsuObjects { MaxCombo = 500 }
			},
			Assets = new BeatmapAssets(),
			UpdatedAt = now,
			CreatedAt = now
		};
		bot.Beatmaps.GetAsync(KnownBeatmapId, Arg.Any<CancellationToken>()).Returns(beatmap);
		return beatmap;
	}
}