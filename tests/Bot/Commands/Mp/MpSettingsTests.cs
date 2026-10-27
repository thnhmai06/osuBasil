using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Domain.Mechanics;
using Basil.Testing;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     The commands that show or change the settings of a room: <c>!mp settings</c>, <c>lock</c>, <c>unlock</c>,
///     <c>private</c>, <c>size</c>, <c>name</c>, <c>password</c> and <c>set</c>. Expected replies are named by the
///     reply that owns the wording.
/// </summary>
public sealed class MpSettingsTests
{
	[Fact]
	public async Task Settings_OfANewRoom_ListsTheRoomTheCreatorAndTheHostInOrder()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		var room = await bot.MakeAsync(typist, "Finals");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp settings");

		Assert.True(succeeded);
		var expected = string.Join('\n',
			bot.Text(MpReplies.SettingsRoomName, new { name = "Finals", room = room.Id }),
			bot.Text(MpReplies.SettingsBeatmapNotSelected),
			bot.Text(MpReplies.SettingsTeamMode, new { teamType = "HeadToHead", winCondition = "Score" }),
			bot.Text(MpReplies.SettingsCreator, new { id = typist.Record.Id, name = "Typist" }),
			bot.Text(MpReplies.SettingsPlayers, new { count = 1 }),
			SlotLine(bot, 1, typist, true));
		Assert.Equal([bot.Prefixed(room.Id, expected)], said.Here);
	}

	[Fact]
	public async Task Settings_WithTwoSeatedPlayers_ListsOneSlotLinePerPlayerInSlotOrder()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		var room = await bot.MakeAsync(typist, "Finals");
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");
		Assert.True((await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}")).Succeeded);

		var (_, said) = await bot.TypeAsync(typist, "#osu", "!mp settings");

		var expected = string.Join('\n',
			bot.Text(MpReplies.SettingsRoomName, new { name = "Finals", room = room.Id }),
			bot.Text(MpReplies.SettingsBeatmapNotSelected),
			bot.Text(MpReplies.SettingsTeamMode, new { teamType = "HeadToHead", winCondition = "Score" }),
			bot.Text(MpReplies.SettingsCreator, new { id = typist.Record.Id, name = "Typist" }),
			bot.Text(MpReplies.SettingsPlayers, new { count = 2 }),
			SlotLine(bot, 1, typist, true),
			SlotLine(bot, 2, joiner, false));
		Assert.Equal([bot.Prefixed(room.Id, expected)], said.Here);
	}

	[Fact]
	public async Task Lock_ByTheCreator_SaysTheMatchIsLockedAndLocksIt()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp lock");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.LockedMatch)], said.Here);
		Assert.True(room.Slots.Locked);
	}

	[Fact]
	public async Task Unlock_ByTheCreatorOfALockedRoom_SaysTheMatchIsUnlockedAndUnlocksIt()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp lock")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp unlock");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UnlockedMatch)], said.Here);
		Assert.False(room.Slots.Locked);
	}

	[Theory]
	[InlineData(false, "public")]
	[InlineData(true, "private")]
	public async Task Private_WithoutAnArgument_SaysWhetherTheRoomIsPrivate(bool isPrivate, string privacy)
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals", isPrivate);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp private");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MatchIsPrivateNow, new { privacy })], said.Here);
	}

	[Fact]
	public async Task Private_WithOne_MakesTheRoomPrivateAndSaysSo()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp private 1");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MatchNowPrivate)], said.Here);
		Assert.True(room.Settings.IsPrivate);
	}

	[Fact]
	public async Task Private_WithZero_MakesTheRoomPublicAndSaysSo()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals", true);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp private 0");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MatchNowPublic)], said.Here);
		Assert.False(room.Settings.IsPrivate);
	}

	[Theory]
	[InlineData("!mp private 2")]
	[InlineData("!mp private abc")]
	public async Task Private_WithSomethingOtherThanZeroOrOne_SaysHowToUseItAndChangesNothing(string message)
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.PrivateUsage)], said.Here);
		Assert.False(room.Settings.IsPrivate);
	}

	[Fact]
	public async Task Size_WithEight_SaysTheNewSizeAndLeavesEightUsableSlots()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp size 8");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.ChangedMatchSize, new { size = 8 })], said.Here);
		Assert.Equal(8, UsableSlots(room));
	}

	[Theory]
	[InlineData("!mp size 0")]
	[InlineData("!mp size 17")]
	[InlineData("!mp size x")]
	[InlineData("!mp size")]
	public async Task Size_OutsideOneToSixteenOrNotANumber_SaysHowToUseItAndChangesNothing(string message)
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.SizeUsage)], said.Here);
		Assert.Equal(16, UsableSlots(room));
	}

	[Fact]
	public async Task Name_WithAText_RenamesTheRoomAndSaysSo()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp name Semifinal");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.RoomNameUpdated, new { name = "Semifinal" })], said.Here);
		Assert.Equal("Semifinal", room.Settings.Name);
	}

	[Fact]
	public async Task Name_WithoutAText_SaysHowToUseItAndKeepsTheName()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp name");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NameUsage)], said.Here);
		Assert.Equal("Finals", room.Settings.Name);
	}

	[Fact]
	public async Task Password_WithAText_SaysItChangedAndMakesJoiningNeedIt()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp password secret");
		var withoutIt = await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}");
		var withIt = await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id} secret");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.ChangedMatchPassword)], said.Here);
		Assert.False(withoutIt.Succeeded);
		Assert.Equal([bot.Text(MpReplies.IncorrectPassword)], withoutIt.Said.Here);
		Assert.True(withIt.Succeeded);
	}

	[Fact]
	public async Task Password_WithoutAText_SaysItWasRemovedAndMakesJoiningNeedNone()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp password secret")).Succeeded);
		var joiner = await RoomFixture.PlayerAsync(bot.App, "Joiner");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp password");
		var join = await bot.TypeAsync(joiner, "#osu", $"!mp join {room.Id}");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.RemovedMatchPassword)], said.Here);
		Assert.True(join.Succeeded);
		Assert.NotNull(room.Slots.Find(joiner.Record));
	}

	[Fact]
	public async Task Set_WithTeamModeWinConditionAndSize_ChangesAllThreeAndSaysWhatTheRoomNowHas()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp set 2 1 8");

		Assert.True(succeeded);
		Assert.Equal(GameTeamType.TeamVs, room.Settings.MatchSettings.TeamType);
		Assert.Equal(GameWinCondition.Accuracy, room.Settings.MatchSettings.WinCondition);
		Assert.Equal(8, UsableSlots(room));
		Assert.Equal([
				bot.TextIn(room.Id, MpReplies.ChangedMatchSettings,
					new
					{
						teamType = room.Settings.MatchSettings.TeamType.ToString(),
						winCondition = room.Settings.MatchSettings.WinCondition.ToString(),
						size = "8"
					})
			],
			said.Here);
	}

	[Fact]
	public async Task Set_WithOnlyTheTeamMode_ChangesItAndSaysNoSize()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp set 2");

		Assert.True(succeeded);
		Assert.Equal(GameTeamType.TeamVs, room.Settings.MatchSettings.TeamType);
		Assert.Equal(16, UsableSlots(room));
		Assert.Equal([
				bot.TextIn(room.Id, MpReplies.ChangedMatchSettings,
					new
					{
						teamType = room.Settings.MatchSettings.TeamType.ToString(),
						winCondition = room.Settings.MatchSettings.WinCondition.ToString(),
						size = "none"
					})
			],
			said.Here);
	}

	[Theory]
	[InlineData("!mp set")]
	[InlineData("!mp set 9")]
	public async Task Set_WithoutAValidTeamMode_SaysHowToUseItAndChangesNothing(string message)
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.SetUsage)], said.Here);
		Assert.Equal(GameTeamType.HeadToHead, room.Settings.MatchSettings.TeamType);
	}

	[Theory]
	[InlineData("!mp lock")]
	[InlineData("!mp private 1")]
	[InlineData("!mp size 8")]
	[InlineData("!mp name Other")]
	[InlineData("!mp password x")]
	[InlineData("!mp set 2")]
	public async Task SettingsChange_ByAPlainPlayer_IsRefusedAndChangesNothing(string message)
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");
		Assert.True((await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}")).Succeeded);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	/// <summary>Counts the slots a player may sit in: the occupied ones and the open ones.</summary>
	private static int UsableSlots(Room room)
	{
		return room.Slots.Count(slot => !slot.Locked);
	}

	/// <summary>Reads everything the settings commands can change, as the room now shows it.</summary>
	private static (bool Locked, bool IsPrivate, int Usable, string Name, string Password, GameTeamType TeamType)
		Snapshot(Room room)
	{
		return (room.Slots.Locked, room.Settings.IsPrivate, UsableSlots(room), room.Settings.Name,
			room.Settings.Password, room.Settings.MatchSettings.TeamType);
	}

	/// <summary>
	///     Writes the <c>!mp settings</c> line of a player who has not touched ready, padding the columns by hand:
	///     the slot number to 2 characters on the right, the status to 10 on the left, the user id to 6 on the right and
	///     the name to 16 on the left.
	/// </summary>
	private static string SlotLine(BotHarness bot, int slot, BanchoConnection player, bool isHost)
	{
		return bot.Text(MpReplies.SettingsSlot, new
		{
			slot = slot.ToString().PadLeft(2),
			status = bot.Text(MpReplies.SlotStatusNotReady).PadRight(10),
			team = "",
			id = player.Record.Id.ToString().PadLeft(6),
			name = player.Record.Value.Name.PadRight(16),
			tags = isHost ? bot.Text(MpReplies.Tags, new { tags = bot.Text(MpReplies.HostTag) }) : ""
		});
	}
}