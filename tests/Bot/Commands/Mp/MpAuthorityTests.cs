using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Tests.Support;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Bot.Tests.Commands.Mp;

/// <summary>
///     The bot acts with the rights of the user who typed the command: managing a room is for its creator, its
///     referees and whoever may manage every room; being the host, a plain player or an outsider grants no
///     management rights. Expected replies are named by the reply that owns the wording.
/// </summary>
public sealed class MpAuthorityTests
{
	[Theory]
	[InlineData(RoomRole.Creator)]
	[InlineData(RoomRole.Referee)]
	[InlineData(RoomRole.Manager)]
	public async Task Lock_ByARoleThatManagesTheRoom_LocksIt(RoomRole role)
	{
		using var bot = new BotHarness();
		var (room, actor) = await SetUpAsync(bot, role);

		var (succeeded, said) = await bot.TypeAsync(actor, "#osu", "!mp lock");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.LockedMatch)], said.Here);
		Assert.True(room.Slots.Locked);
	}

	[Theory]
	[InlineData(RoomRole.Host)]
	[InlineData(RoomRole.Player)]
	[InlineData(RoomRole.Outsider)]
	public async Task Lock_ByARoleThatDoesNotManageTheRoom_IsRefusedAndTheRoomStaysUnlocked(RoomRole role)
	{
		using var bot = new BotHarness();
		var (room, actor) = await SetUpAsync(bot, role);

		var (succeeded, said) = await bot.TypeAsync(actor, "#osu", "!mp lock");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.False(room.Slots.Locked);
	}

	[Theory]
	[InlineData(RoomRole.Creator)]
	[InlineData(RoomRole.Referee)]
	[InlineData(RoomRole.Manager)]
	public async Task Close_ByARoleThatManagesTheRoom_ClosesIt(RoomRole role)
	{
		using var bot = new BotHarness();
		var (room, actor) = await SetUpAsync(bot, role);

		var (succeeded, said) = await bot.TypeAsync(actor, "#osu", "!mp close");

		Assert.True(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.ClosedMatch)], said.Here);
		Assert.True(room.IsClosed);
	}

	[Theory]
	[InlineData(RoomRole.Host)]
	[InlineData(RoomRole.Player)]
	[InlineData(RoomRole.Outsider)]
	public async Task Close_ByARoleThatDoesNotManageTheRoom_IsRefusedAndTheRoomStaysOpen(RoomRole role)
	{
		using var bot = new BotHarness();
		var (room, actor) = await SetUpAsync(bot, role);

		var (succeeded, said) = await bot.TypeAsync(actor, "#osu", "!mp close");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.NotAuthorizedInMatch)], said.Here);
		Assert.False(room.IsClosed);
	}

	[Fact]
	public async Task Lock_ByTheCreatorWhoMayNotJoinRooms_IsNotElevatedByTheBotAndNamesTheMissingPermission()
	{
		using var bot = new BotHarness();
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator", UserPermissions.RoomCreate);
		var room = await bot.MakeAsync(creator, "Finals");
		Assert.Equal(creator.Record, room.Authority.Creator);

		var (succeeded, said) = await bot.TypeAsync(creator, "#osu", "!mp lock");

		Assert.False(succeeded);
		Assert.Equal([bot.TextIn(room.Id, MpReplies.MissingPermissions, new { permissions = "RoomJoin" })], said.Here);
		Assert.False(room.Slots.Locked);
	}

	/// <summary>
	///     Opens a room with <c>!mp make</c> as its creator and puts a user with a role in it, using only commands,
	///     then has that user target the room with <c>!mp in</c>.
	/// </summary>
	private static async Task<(Room Room, BanchoConnection Actor)> SetUpAsync(BotHarness bot, RoomRole role)
	{
		var creator = await RoomFixture.PlayerAsync(bot.App, "Creator");
		var room = await bot.MakeAsync(creator, "Finals");

		BanchoConnection actor;
		switch (role)
		{
			case RoomRole.Creator:
				actor = creator;
				break;
			case RoomRole.Referee:
				actor = await RoomFixture.PlayerAsync(bot.App, "Referee");
				Assert.True((await bot.TypeAsync(creator, "#osu", "!mp addref Referee")).Succeeded);
				break;
			case RoomRole.Host:
				actor = await RoomFixture.PlayerAsync(bot.App, "Host");
				Assert.True((await bot.TypeAsync(actor, "#osu", $"!mp join {room.Id}")).Succeeded);
				Assert.True((await bot.TypeAsync(creator, "#osu", "!mp host Host")).Succeeded);
				Assert.Same(actor, room.Authority.Host);
				break;
			case RoomRole.Player:
				actor = await RoomFixture.PlayerAsync(bot.App, "Player");
				Assert.True((await bot.TypeAsync(actor, "#osu", $"!mp join {room.Id}")).Succeeded);
				break;
			case RoomRole.Outsider:
				actor = await RoomFixture.PlayerAsync(bot.App, "Outsider");
				break;
			case RoomRole.Manager:
				actor = await RoomFixture.PlayerAsync(bot.App, "Manager",
					UserPermissionsGroups.Default | UserPermissions.RoomManageAny);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(role), role, "This role is not part of the scenario.");
		}

		Assert.True((await bot.TypeAsync(actor, null, $"!mp in {room.Id}")).Succeeded);
		return (room, actor);
	}
}