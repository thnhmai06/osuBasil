using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Testing;

/// <summary>The roles a caller can hold in a room, for the authority tests.</summary>
public enum RoomRole
{
	/// <summary>The user who created the room.</summary>
	Creator,

	/// <summary>A referee of the room.</summary>
	Referee,

	/// <summary>The seated player who is the room's host.</summary>
	Host,

	/// <summary>A seated player who is not the host.</summary>
	Player,

	/// <summary>A user who is logged in but takes no part in the room.</summary>
	Outsider,

	/// <summary>A user who may manage every room.</summary>
	Manager,

	/// <summary>A user who was never granted the right to join rooms.</summary>
	NoRoomJoin,

	/// <summary>The creator of a room whose right to join rooms is suspended.</summary>
	SuspendedCreator
}

/// <summary>
///     A room and the connections that act in it, with one connection per <see cref="RoomRole" />.
/// </summary>
/// <remarks>
///     The room is opened in game, so its creator is seated and hosts it; the host is then given to another
///     seated player, which leaves the creator with the creator role alone and the two players apart.
/// </remarks>
public sealed class RoomFixture
{
	/// <summary>Gets a beatmap a room may select.</summary>
	public static BeatmapReference Beatmap { get; } =
		new(new Md5("0123456789abcdef0123456789abcdef"), 1, "beatmap", GameMode.Standard, null);

	/// <summary>Gets the room the roles act in.</summary>
	public Room Room { get; private init; } = null!;

	/// <summary>Gets the tournament room the creator whose right to join rooms is suspended opened.</summary>
	public Room SuspendedRoom { get; private init; } = null!;

	/// <summary>Gets the connection that created the room.</summary>
	public BanchoConnection Creator { get; private init; } = null!;

	/// <summary>Gets the connection of a referee, which is not seated.</summary>
	public BanchoConnection Referee { get; private init; } = null!;

	/// <summary>Gets the connection that is the room's host.</summary>
	public BanchoConnection Host { get; private init; } = null!;

	/// <summary>Gets the connection that is seated but is not the host.</summary>
	public BanchoConnection Player { get; private init; } = null!;

	/// <summary>Gets the connection of a user who takes no part in the room.</summary>
	public BanchoConnection Outsider { get; private init; } = null!;

	/// <summary>Gets the connection of a user who may manage every room.</summary>
	public BanchoConnection Manager { get; private init; } = null!;

	/// <summary>Gets the connection of a user who was never granted the right to join rooms.</summary>
	public BanchoConnection NoRoomJoin { get; private init; } = null!;

	/// <summary>Gets the connection of the creator whose right to join rooms is suspended.</summary>
	public BanchoConnection SuspendedCreator { get; private init; } = null!;

	/// <summary>Creates a score played in a round.</summary>
	/// <param name="id">The score's id.</param>
	/// <param name="round">The round the score was played in.</param>
	/// <returns>The score.</returns>
	public static RoundScore Score(int id, Round round)
	{
		var score = new ScoreRecord
		{
			Id = id,
			Value = new ScoreData(
				null,
				round.BeatmapHash,
				GameMode.Standard,
				GameMods.NoMod,
				new HitCounts(1, 0, 0, 0, 0, 0),
				1000,
				10,
				0,
				Grade.A,
				true,
				false,
				DateTimeOffset.UnixEpoch)
		};
		return new RoundScore(score, round, null);
	}

	/// <summary>Creates a stored score of a player.</summary>
	/// <param name="id">The score's id.</param>
	/// <param name="player">The player who set the score.</param>
	/// <param name="beatmap">The beatmap played.</param>
	/// <param name="totalScore">The total score.</param>
	/// <returns>The score.</returns>
	public static ScoreRecord PlayerScore(int id, UserRecord player, Md5 beatmap, int totalScore = 1000)
	{
		return new ScoreRecord
		{
			Id = id,
			Value = new ScoreData(
				player.Id,
				beatmap,
				GameMode.Standard,
				GameMods.NoMod,
				new HitCounts(1, 0, 0, 0, 0, 0),
				totalScore,
				10,
				0,
				Grade.A,
				true,
				false,
				DateTimeOffset.UnixEpoch)
		};
	}

	/// <summary>Opens the rooms and the connections the roles act with.</summary>
	/// <param name="app">The application the rooms live in.</param>
	/// <returns>The fixture.</returns>
	public static async Task<RoomFixture> CreateAsync(TestApplication app)
	{
		var creator = await PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", false, false,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(opened.IsOk);
		Assert.NotNull(room);

		var host = await PlayerAsync(app, "Host");
		Assert.True((await room.ScopeTo(host).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);

		var player = await PlayerAsync(app, "Player");
		Assert.True((await room.ScopeTo(player).Members.JoinAsync("", TestContext.Current.CancellationToken)).IsOk);

		var referee = await PlayerAsync(app, "Referee");
		Assert.True((await room.ScopeTo(creator).Authority
			.AddRefereeAsync(referee.Record, TestContext.Current.CancellationToken)).IsOk);

		Assert.True((await room.ScopeTo(creator).Authority
			.SetHostAsync(host, TestContext.Current.CancellationToken)).IsOk);

		var outsider = await PlayerAsync(app, "Outsider");
		var manager = await PlayerAsync(app, "Manager", UserPermissionsGroups.Default | UserPermissions.RoomManageAny);
		var noRoomJoin = await PlayerAsync(app, "NoJoin", UserPermissionsGroups.Default & ~UserPermissions.RoomJoin);

		// The suspended creator may create a tournament room, but may not be seated in it.
		var suspendedUser = await app.CreateUserAsync("Suspended", "password");
		app.AddRestriction(suspendedUser, UserPermissions.RoomJoin);
		var suspendedCreator = await PlayerAsync(app, suspendedUser);
		var (suspendedRoom, suspendedOpened) = await app.Lobby.ScopeTo(suspendedCreator)
			.OpenRoomAsync("tournament room", "", true, false,
				cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(suspendedOpened.IsOk);
		Assert.NotNull(suspendedRoom);

		return new RoomFixture
		{
			Room = room,
			SuspendedRoom = suspendedRoom,
			Creator = creator,
			Referee = referee,
			Host = host,
			Player = player,
			Outsider = outsider,
			Manager = manager,
			NoRoomJoin = noRoomJoin,
			SuspendedCreator = suspendedCreator
		};
	}

	/// <summary>Gets the connection that holds a role.</summary>
	/// <param name="role">The role.</param>
	/// <returns>The connection.</returns>
	public BanchoConnection ConnectionFor(RoomRole role)
	{
		return role switch
		{
			RoomRole.Creator => Creator,
			RoomRole.Referee => Referee,
			RoomRole.Host => Host,
			RoomRole.Player => Player,
			RoomRole.Outsider => Outsider,
			RoomRole.Manager => Manager,
			RoomRole.NoRoomJoin => NoRoomJoin,
			RoomRole.SuspendedCreator => SuspendedCreator,
			_ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown room role.")
		};
	}

	/// <summary>Gets the room a role acts in.</summary>
	/// <param name="role">The role.</param>
	/// <returns>The room.</returns>
	public Room RoomFor(RoomRole role)
	{
		return role == RoomRole.SuspendedCreator ? SuspendedRoom : Room;
	}

	/// <summary>Creates a user and logs them in with an osu! client.</summary>
	/// <param name="app">The application the user belongs to.</param>
	/// <param name="name">The account name.</param>
	/// <param name="permissions">The permissions to grant, or the normal new-account permissions.</param>
	/// <returns>The opened connection.</returns>
	public static async Task<BanchoConnection> PlayerAsync(TestApplication app, string name,
		UserPermissions? permissions = null)
	{
		var user = await app.CreateUserAsync(name, "password", permissions);
		return await PlayerAsync(app, user);
	}

	/// <summary>Logs a stored account in with an osu! client.</summary>
	/// <param name="app">The application the user belongs to.</param>
	/// <param name="user">The account to log in.</param>
	/// <returns>The opened connection.</returns>
	public static async Task<BanchoConnection> PlayerAsync(TestApplication app, UserRecord user)
	{
		return (BanchoConnection)await app.LoginAsync(user, ConnectionType.Bancho);
	}
}