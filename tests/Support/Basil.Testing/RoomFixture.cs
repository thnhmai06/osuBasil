using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Game.Scores;
using Basil.Domain.Game.Users;
using Basil.Domain.Multiplayer;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Testing;

public enum RoomRole
{
	Creator,

	Referee,

	Host,

	Player,

	Outsider,

	Manager,

	NoRoomJoin,

	SuspendedCreator
}

public sealed class RoomFixture
{
	public static BeatmapReference Beatmap { get; } =
		new(new Md5Hash("0123456789abcdef0123456789abcdef"), 1, "beatmap", GameMode.Standard, null);

	public Room Room { get; private init; } = null!;

	public Room SuspendedRoom { get; private init; } = null!;

	public StableConnection Creator { get; private init; } = null!;

	public StableConnection Referee { get; private init; } = null!;

	public StableConnection Host { get; private init; } = null!;

	public StableConnection Player { get; private init; } = null!;

	public StableConnection Outsider { get; private init; } = null!;

	public StableConnection Manager { get; private init; } = null!;

	public StableConnection NoRoomJoin { get; private init; } = null!;

	public StableConnection SuspendedCreator { get; private init; } = null!;

	public static RoundScore Score(int id, Round round)
	{
		var score = new ScoreRecord
		{
			Id = id,
			Value = new ScoreData(null, round.BeatmapHash, GameMode.Standard, GameMods.NoMod,
				new ScoreState(new HitCounts(1, 0, 0, 0, 0, 0), 1000, 10, 0, false), Grade.A, true,
				DateTimeOffset.UnixEpoch)
		};
		return new RoundScore(score, round, null);
	}

	public static ScoreRecord PlayerScore(int id, UserRecord player, Md5Hash beatmap, int totalScore = 1000)
	{
		return new ScoreRecord
		{
			Id = id,
			Value = new ScoreData(player.Id, beatmap, GameMode.Standard, GameMods.NoMod,
				new ScoreState(new HitCounts(1, 0, 0, 0, 0, 0), totalScore, 10, 0, false), Grade.A, true,
				DateTimeOffset.UnixEpoch)
		};
	}

	public static async Task<RoomFixture> CreateAsync(TestApplication app)
	{
		var creator = await PlayerAsync(app, "Creator");
		var (room, opened) = await app.Lobby.ScopeTo(creator).OpenRoomAsync("test room", "", RoomSource.Game, false);
		Assert.True(opened == RoomOutcome.Ok);
		Assert.NotNull(room);

		var host = await PlayerAsync(app, "Host");
		Assert.True(await room.ScopeTo(host).Members.JoinAsync("") == RoomOutcome.Ok);

		var player = await PlayerAsync(app, "Player");
		Assert.True(await room.ScopeTo(player).Members.JoinAsync("") == RoomOutcome.Ok);

		var referee = await PlayerAsync(app, "Referee");
		Assert.True(await room.ScopeTo(creator).Authority
			.AddRefereeAsync(UserReference.ById(referee.Record.Id)) == RoomOutcome.Ok);

		Assert.True(await room.ScopeTo(creator).Authority
			.SetHostAsync(host) == RoomOutcome.Ok);

		var outsider = await PlayerAsync(app, "Outsider");
		var manager = await PlayerAsync(app, "Manager", UserPrivilegesExtensions.Default | UserPrivileges.Tournament);
		var noRoomJoin = await PlayerAsync(app, "NoJoin", UserPrivilegesExtensions.Default & ~UserPrivileges.Player);

		// The creator who lost the player privilege after creating the room may not be seated in it.
		var suspendedUser = await app.CreateUserAsync("Suspended", "password");
		var suspendedCreator = await PlayerAsync(app, suspendedUser);
		var (suspendedRoom, suspendedOpened) = await app.Lobby.ScopeTo(suspendedCreator)
			.OpenRoomAsync("api room", "", RoomSource.Api, false);
		Assert.True(suspendedOpened == RoomOutcome.Ok);
		Assert.NotNull(suspendedRoom);
		suspendedUser.Value.Privileges = UserPrivileges.None;

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

	public StableConnection ConnectionFor(RoomRole role)
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

	public Room RoomFor(RoomRole role)
	{
		return role == RoomRole.SuspendedCreator ? SuspendedRoom : Room;
	}

	public static async Task<StableConnection> PlayerAsync(TestApplication app, string name,
		UserPrivileges? privileges = null)
	{
		var user = await app.CreateUserAsync(name, "password", privileges);
		return await PlayerAsync(app, user);
	}

	public static async Task<StableConnection> PlayerAsync(TestApplication app, UserRecord user)
	{
		return (StableConnection)await app.LoginAsync(user, SessionType.Stable);
	}
}