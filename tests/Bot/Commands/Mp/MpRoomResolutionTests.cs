using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Domain.Permissions;
using Basil.Testing;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     Which room a room-scoped <c>!mp</c> command applies to: the room of the channel, else the room remembered by
///     <c>!mp in</c>, else the room the typist plays in. Expected replies are named by the reply that owns the wording.
/// </summary>
public sealed class MpRoomResolutionTests
{
	/// <summary>Everyone who must lock a room they do not create can do so with this, wherever they sit.</summary>
	private static readonly Permissions ManagerPermissions = PermissionsGroups.Default | Permissions.RoomManageAny;

	[Fact]
	public async Task RoomScopedCommand_WithNoRoomAnywhere_SaysNotInARoom()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp lock");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NotInARoom)], said.Here);
		Assert.Empty(bot.App.Lobby.Rooms);
	}

	[Fact]
	public async Task RoomScopedCommand_TypedInARoomChannel_AppliesToThatRoomEvenWhenSeatedInAnotherAndTargetingAThird()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var seatedIn = await bot.MakeAsync(owner, "Seated");
		var targeted = await bot.MakeAsync(owner, "Targeted");
		var channelRoom = await bot.MakeAsync(owner, "Channel");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist", ManagerPermissions);
		Assert.True((await seatedIn.ScopeTo(typist).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await bot.TypeAsync(typist, null, $"!mp in {targeted.Id}")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(typist, $"#mp_{channelRoom.Id}", "!mp lock");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(MpReplies.LockedMatch)], said.Here);
		Assert.True(channelRoom.Slots.Locked);
		Assert.False(seatedIn.Slots.Locked);
		Assert.False(targeted.Slots.Locked);
	}

	[Fact]
	public async Task RoomScopedCommand_TypedInARoomChannel_AppliesToThatRoomWithoutASeatOrATarget()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var first = await bot.MakeAsync(owner, "First");
		var second = await bot.MakeAsync(owner, "Second");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist", ManagerPermissions);

		var (_, said) = await bot.TypeAsync(typist, $"#mp_{second.Id}", "!mp lock");

		Assert.Equal([bot.Text(MpReplies.LockedMatch)], said.Here);
		Assert.True(second.Slots.Locked);
		Assert.False(first.Slots.Locked);
	}

	[Fact]
	public async Task RoomScopedCommand_WithARememberedRoom_AppliesToItInsteadOfTheSeatedRoom()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var seatedIn = await bot.MakeAsync(owner, "Seated");
		var targeted = await bot.MakeAsync(owner, "Targeted");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist", ManagerPermissions);
		Assert.True((await seatedIn.ScopeTo(typist).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await bot.TypeAsync(typist, null, $"!mp in {targeted.Id}")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp lock");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(targeted.Id, MpReplies.LockedMatch)], said.Here);
		Assert.True(targeted.Slots.Locked);
		Assert.False(seatedIn.Slots.Locked);
	}

	[Fact]
	public async Task RoomScopedCommand_WithOnlyASeat_AppliesToTheSeatedRoom()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Seated");
		var referee = await RoomFixture.PlayerAsync(bot.App, "Referee");
		Assert.True((await room.ScopeTo(owner).Authority.AddRefereeAsync(referee.Record,
			TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await room.ScopeTo(referee).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);
		Assert.Null(bot.Targets.Get(referee.Record.Id));

		var (succeeded, said) = await bot.TypeAsync(referee, "#osu", "!mp lock");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.LockedMatch)], said.Here);
		Assert.True(room.Slots.Locked);
	}

	[Fact]
	public async Task RoomScopedCommand_AfterTheRememberedRoomClosed_SaysNotInARoom()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		Assert.True((await bot.TypeAsync(typist, null, $"!mp in {room.Id}")).Succeeded);
		Assert.True((await bot.TypeAsync(owner, "#osu", "!mp close")).Succeeded);

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp lock");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NotInARoom)], said.Here);
	}

	[Fact]
	public async Task RoomScopedCommand_AfterTheRememberedRoomClosed_ForgetsTheRoom()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Finals");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		Assert.True((await bot.TypeAsync(typist, null, $"!mp in {room.Id}")).Succeeded);
		Assert.True((await bot.TypeAsync(owner, "#osu", "!mp close")).Succeeded);

		await bot.TypeAsync(typist, "#osu", "!mp lock");

		Assert.Null(bot.Targets.Get(typist.Record.Id));
	}

	[Fact]
	public async Task RoomScopedCommand_AfterTheRememberedRoomsIdIsReused_DoesNotApplyToTheNewRoom()
	{
		using var bot = new BotHarness();
		var firstOwner = await RoomFixture.PlayerAsync(bot.App, "FirstOwner");
		var oldRoom = await bot.MakeAsync(firstOwner, "Old");
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist", ManagerPermissions);
		Assert.True((await bot.TypeAsync(typist, null, $"!mp in {oldRoom.Id}")).Succeeded);
		Assert.True((await bot.TypeAsync(firstOwner, "#osu", "!mp close")).Succeeded);
		var secondOwner = await RoomFixture.PlayerAsync(bot.App, "SecondOwner");
		var newRoom = await bot.MakeAsync(secondOwner, "New");
		Assert.Equal(oldRoom.Id, newRoom.Id);

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp lock");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NotInARoom)], said.Here);
		Assert.False(newRoom.Slots.Locked);
	}

	[Fact]
	public async Task In_ForAPrivateRoomOfAStranger_SaysNoActiveRoomAndRemembersNothing()
	{
		using var bot = new BotHarness();
		var owner = await RoomFixture.PlayerAsync(bot.App, "Owner");
		var room = await bot.MakeAsync(owner, "Secret", true);
		var stranger = await RoomFixture.PlayerAsync(bot.App, "Stranger");

		var (succeeded, said) = await bot.TypeAsync(stranger, null, $"!mp in {room.Id}");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(MpReplies.NoActiveRoomWithHashId, new { room = room.Id })], said.Here);
		Assert.Null(bot.Targets.Get(stranger.Record.Id));
	}

	[Fact]
	public async Task UnknownSubcommand_WhenTheTypistHasARoom_SaysItIsUnknownAndFails()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");
		var room = await bot.MakeAsync(typist, "Finals");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", "!mp frobnicate");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.UnknownMpSubcommand, new { subcommand = "frobnicate" })],
			said.Here);
	}

	[Theory]
	[InlineData("!mp")]
	[InlineData("!mp help")]
	public async Task Help_WhisperedToTheTypist_SaysNothingWhereItWasTyped(string message)
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Typist");

		var (succeeded, said) = await bot.TypeAsync(typist, "#osu", message);

		Assert.True(succeeded);
		Assert.Empty(said.Here);
		Assert.Equal([bot.Text(MpReplies.HelpText)], said.Privately);
	}
}
