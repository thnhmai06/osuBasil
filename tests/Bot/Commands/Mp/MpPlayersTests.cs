using Basil.Application.Users.Objects.Connections;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Domain.Mechanics;
using Basil.Testing;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     The commands that act on the people in a room: <c>!mp move</c>, <c>host</c>, <c>clearhost</c>, <c>invite</c>,
///     <c>kick</c>, <c>ban</c>, <c>unban</c> and <c>team</c>. Expected replies are named by the reply that owns the
///     wording.
/// </summary>
public sealed class MpPlayersTests
{
	[Theory]
	[InlineData("!mp move")]
	[InlineData("!mp move Player")]
	public async Task Move_WithoutANameAndASlot_SaysHowToUseItAndMovesNobody(string message)
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MoveUsage)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Fact]
	public async Task Move_ForANameNoAccountHas_SaysTheUserWasNotFound()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp move Nobody 3");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UserNotFound)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Move_ForARegisteredUserWhoIsNotSeated_SaysTheUserIsNotInTheMatch(bool online)
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		if (online) await RoomFixture.PlayerAsync(bot.App, "Stranger");
		else await bot.App.CreateUserAsync("Stranger", "password");
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp move Stranger 3");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TargetNotInMatch)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Fact]
	public async Task Move_OfASeatedPlayerToAFreeSlot_SaysSoAndSeatsThePlayerThere()
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp move Player 3");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MovedToSlot, new { player = "Player", slot = 3 })], said.Here);
		Assert.Equal(3, room.Slots.Find(player.Record)!.Index);
	}

	[Fact]
	public async Task Move_WithTheUserIdAfterAHash_MovesThatUser()
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", $"!mp move #{player.Record.Id} 3");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MovedToSlot, new { player = "Player", slot = 3 })], said.Here);
		Assert.Equal(3, room.Slots.Find(player.Record)!.Index);
	}

	[Fact]
	public async Task Move_ToASlotThatIsTaken_SaysItIsNotOpenAndKeepsEveryoneSeated()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp move Player 1");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.DestinationSlotNotOpen)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Theory]
	[InlineData("17")]
	[InlineData("0")]
	[InlineData("x")]
	public async Task Move_ToASlotOutsideOneToSixteenOrNotANumber_SaysHowToUseItAndKeepsThePlayerSeated(string slot)
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", $"!mp move Player {slot}");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MoveUsage)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Fact]
	public async Task Host_WithoutAName_SaysHowToUseItAndKeepsTheHost()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp host");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.HostUsage)], said.Here);
		Assert.Same(creator, room.Authority.Host);
	}

	[Fact]
	public async Task Host_ForASeatedPlayer_SaysSoAndMakesThePlayerTheHost()
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp host Player");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.ChangedMatchHost, new { player = "Player" })], said.Here);
		Assert.Same(player, room.Authority.Host);
	}

	[Fact]
	public async Task Host_ForANameNoAccountHas_SaysTheUserWasNotFoundAndKeepsTheHost()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp host Nobody");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UserNotFound)], said.Here);
		Assert.Same(creator, room.Authority.Host);
	}

	[Fact]
	public async Task ClearHost_SaysTheHostWasClearedAndLeavesTheMatchWithoutAHost()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp clearhost");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.ClearedMatchHost)], said.Here);
		Assert.Null(room.Authority.Host);
	}

	[Fact]
	public async Task Invite_WithoutAName_SaysHowToUseIt()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp invite");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.InviteUsage)], said.Here);
	}

	[Fact]
	public async Task Invite_OfAnAccountThatIsOffline_SaysThePlayerIsNotOnline()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		await bot.App.CreateUserAsync("Offline", "password");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp invite Offline");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TargetNotOnline)], said.Here);
	}

	[Fact]
	public async Task Invite_OfANameNoAccountHas_SaysTheUserWasNotFound()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp invite Nobody");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UserNotFound)], said.Here);
	}

	[Fact]
	public async Task Invite_OfAnOnlineUser_SaysTheUserWasInvited()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		await RoomFixture.PlayerAsync(bot.App, "Stranger");

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp invite Stranger");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.InvitedToRoom, new { player = "Stranger" })], said.Here);
	}

	[Fact]
	public async Task Invite_OfAUserWhoIsSeated_SaysTheUserIsAlreadyInTheRoom()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp invite Player");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UserAlreadyInRoom)], said.Here);
	}

	[Fact]
	public async Task Invite_ByAPlainSeatedPlayer_IsAllowed()
	{
		using var bot = new BotHarness();
		var (room, _, player) = await SetUpAsync(bot);
		await RoomFixture.PlayerAsync(bot.App, "Stranger");

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", "!mp invite Stranger");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.InvitedToRoom, new { player = "Stranger" })], said.Here);
	}

	[Fact]
	public async Task Kick_WithoutAName_SaysHowToUseIt()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp kick");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.KickUsage)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Fact]
	public async Task Kick_OfASeatedPlayer_SaysSoAndTakesThePlayerOutOfTheirSlot()
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp kick Player");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.KickedFromMatch, new { player = "Player" })], said.Here);
		Assert.Null(room.Slots.Find(player.Record));
	}

	[Theory]
	[InlineData("Creator")]
	[InlineData("Referee")]
	public async Task Kick_OfTheCreatorOrAReferee_SaysThatCannotBeDoneAndKeepsThemSeated(string target)
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		var referee = await RoomFixture.PlayerAsync(bot.App, "Referee");
		Assert.True((await bot.TypeAsync(referee, "#osu", $"!mp join {room.Id}")).Succeeded);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp addref Referee")).Succeeded);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", $"!mp kick {target}");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TargetIsManager)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Fact]
	public async Task Kick_OfAUserWhoIsNotInTheMatch_SaysThePlayerIsNotInTheMatch()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		await RoomFixture.PlayerAsync(bot.App, "Stranger");
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp kick Stranger");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TargetNotInMatch)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Fact]
	public async Task Ban_WithoutAName_SaysHowToUseIt()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp ban");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.BanUsage)], said.Here);
		Assert.Empty(room.Members.Banned);
	}

	[Fact]
	public async Task Ban_OfASeatedPlayer_SaysSoTakesThePlayerOutAndListsThemAsBanned()
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp ban Player");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.BannedPlayerFromMatch, new { player = "Player" })], said.Here);
		Assert.Null(room.Slots.Find(player.Record));
		Assert.Contains(player.Record, room.Members.Banned);
	}

	[Fact]
	public async Task Join_ByAPlayerBannedFromTheMatch_SaysTheyAreBannedAndDoesNotSeatThem()
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp ban Player")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.BannedFromMatch)], said.Here);
		Assert.Null(room.Slots.Find(player.Record));
	}

	[Fact]
	public async Task Ban_ForANameNoAccountHas_SaysTheUserIsNotRegistered()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp ban Nobody");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UserNotRegistered)], said.Here);
		Assert.Empty(room.Members.Banned);
	}

	[Fact]
	public async Task Unban_WithoutAName_SaysHowToUseIt()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp unban");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UnbanUsage)], said.Here);
	}

	[Fact]
	public async Task Unban_OfABannedPlayer_SaysSoAndRemovesThemFromTheBannedList()
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp ban Player")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp unban Player");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UnbannedFromMatch, new { player = "Player" })], said.Here);
		Assert.DoesNotContain(player.Record, room.Members.Banned);
	}

	[Fact]
	public async Task Join_ByAnUnbannedPlayer_SeatsThemAgain()
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp ban Player")).Succeeded);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp unban Player")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.JoinedMatch, new { room = room.Id, name = "Finals" })], said.Here);
		Assert.NotNull(room.Slots.Find(player.Record));
	}

	[Fact]
	public async Task Unban_OfAPlayerWhoIsNotBanned_SaysThePlayerIsNotBanned()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp unban Player");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TargetNotBanned)], said.Here);
	}

	[Theory]
	[InlineData("!mp team")]
	[InlineData("!mp team Player")]
	[InlineData("!mp team Player green")]
	public async Task Team_WithoutANameAndRedOrBlue_SaysHowToUseItAndChangesNoTeam(string message)
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp set 2")).Succeeded);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.TeamUsage)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Theory]
	[InlineData("red", "Red", GameTeam.Red, "blue")]
	[InlineData("blue", "Blue", GameTeam.Blue, "red")]
	public async Task Team_InATeamMatch_SaysSoAndPutsThePlayerOnThatTeam(string input, string label, GameTeam team,
		string otherTeam)
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp set 2")).Succeeded);
		Assert.True((await bot.TypeAsync(creator, "#osu", $"!mp team Player {otherTeam}")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", $"!mp team Player {input}");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MovedToTeam, new { player = "Player", team = label })], said.Here);
		Assert.Equal(team, room.Slots.Find(player.Record)!.Team);
	}

	[Fact]
	public async Task Team_InAHeadToHeadMatch_SaysTheMatchHasNoTeams()
	{
		using var bot = new BotHarness();
		var (room, creator, _) = await SetUpAsync(bot);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp team Player red");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MatchHasNoTeams)], said.Here);
		Assert.Equal(before, Snapshot(room));
	}

	[Theory]
	[InlineData("!mp move Player 5")]
	[InlineData("!mp host Player")]
	[InlineData("!mp clearhost")]
	[InlineData("!mp kick Creator")]
	[InlineData("!mp ban Creator")]
	[InlineData("!mp unban Creator")]
	[InlineData("!mp team Player red")]
	public async Task PlayerCommand_ByAPlainSeatedPlayer_IsRefusedAndChangesNothing(string message)
	{
		using var bot = new BotHarness();
		var (room, creator, player) = await SetUpAsync(bot);
		Assert.True((await bot.TypeAsync(creator, "#osu", "!mp set 2")).Succeeded);
		var before = Snapshot(room);

		var (succeeded, said) = await bot.TypeAsync(player, "#osu", message);

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.Equal(before, Snapshot(room));
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

	/// <summary>Reads everything these commands can change: the host, who is banned, and who sits where in which team.</summary>
	private static (BanchoConnection? Host, int Banned, string Seats) Snapshot(Room room)
	{
		var seats = room.Slots.Where(slot => slot.Player is not null)
			.Select(slot => $"{slot.Index}:{slot.Player!.Record.Id}:{slot.Team}");
		return (room.Authority.Host, room.Members.Banned.Count, string.Join(',', seats));
	}
}