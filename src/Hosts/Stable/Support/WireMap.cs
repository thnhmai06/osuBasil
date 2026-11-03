using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Beatmaps;
using Basil.Domain.Client;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Protocol.Stable.Models.Auth;
using Basil.Protocol.Stable.Models.Wire;

namespace Basil.Host.Stable.Support;

/// <summary>
///     Translates Domain values to the numbers the osu! client uses and back; the one place where the two numberings
///     meet, so a change to a Domain value never changes the wire.
/// </summary>
/// <remarks>
///     <c>ToWire</c> throws <see cref="ArgumentOutOfRangeException" /> for a Domain value that has no wire
///     counterpart. <c>FromWire</c> returns <see langword="null" /> for a wire value that Domain does not know.
/// </remarks>
internal static class WireMap
{
	private static readonly (GameMods Domain, WireMods Wire)[] Mods =
	[
		(GameMods.NoFail, WireMods.NoFail), (GameMods.Easy, WireMods.Easy),
		(GameMods.TouchScreen, WireMods.TouchScreen), (GameMods.Hidden, WireMods.Hidden),
		(GameMods.HardRock, WireMods.HardRock), (GameMods.SuddenDeath, WireMods.SuddenDeath),
		(GameMods.DoubleTime, WireMods.DoubleTime), (GameMods.Relax, WireMods.Relax),
		(GameMods.HalfTime, WireMods.HalfTime), (GameMods.Nightcore, WireMods.Nightcore),
		(GameMods.Flashlight, WireMods.Flashlight), (GameMods.Autoplay, WireMods.Autoplay),
		(GameMods.SpunOut, WireMods.SpunOut), (GameMods.Autopilot, WireMods.Autopilot),
		(GameMods.Perfect, WireMods.Perfect), (GameMods.Key4, WireMods.Key4), (GameMods.Key5, WireMods.Key5),
		(GameMods.Key6, WireMods.Key6), (GameMods.Key7, WireMods.Key7), (GameMods.Key8, WireMods.Key8),
		(GameMods.FadeIn, WireMods.FadeIn), (GameMods.Random, WireMods.Random), (GameMods.Cinema, WireMods.Cinema),
		(GameMods.Target, WireMods.Target), (GameMods.Key9, WireMods.Key9), (GameMods.KeyCoop, WireMods.KeyCoop),
		(GameMods.Key1, WireMods.Key1), (GameMods.Key3, WireMods.Key3), (GameMods.Key2, WireMods.Key2),
		(GameMods.ScoreV2, WireMods.ScoreV2), (GameMods.Mirror, WireMods.Mirror)
	];

	private static readonly (UserPrivileges Domain, WireClientPrivileges Wire)[] Privileges =
	[
		(UserPrivileges.Player, WireClientPrivileges.Player),
		(UserPrivileges.Moderator, WireClientPrivileges.Moderator),
		(UserPrivileges.Supporter, WireClientPrivileges.Supporter),
		(UserPrivileges.Owner, WireClientPrivileges.Owner),
		(UserPrivileges.Developer, WireClientPrivileges.Developer),
		(UserPrivileges.Tournament, WireClientPrivileges.Tournament)
	];

	/// <summary>Gets the number the client uses for a game mode.</summary>
	/// <param name="mode">The game mode.</param>
	/// <returns>The client's game mode.</returns>
	public static WireGameMode ToWire(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => WireGameMode.Standard,
			GameMode.Taiko => WireGameMode.Taiko,
			GameMode.Catch => WireGameMode.Catch,
			GameMode.Mania => WireGameMode.Mania,
			_ => throw Unmapped(mode)
		};
	}

	/// <summary>Gets the game mode a client number stands for.</summary>
	/// <param name="mode">The client's game mode.</param>
	/// <returns>The game mode, or <see langword="null" /> when the number is not a game mode.</returns>
	public static GameMode? FromWire(WireGameMode mode)
	{
		return mode switch
		{
			WireGameMode.Standard => GameMode.Standard,
			WireGameMode.Taiko => GameMode.Taiko,
			WireGameMode.Catch => GameMode.Catch,
			WireGameMode.Mania => GameMode.Mania,
			_ => null
		};
	}

	/// <summary>Gets the number the client uses for the team of a slot.</summary>
	/// <param name="team">The slot's team, or <see langword="null" /> when it is on none.</param>
	/// <returns>The client's slot team.</returns>
	public static WireSlotTeam ToWire(GameTeam? team)
	{
		return team switch
		{
			null => WireSlotTeam.Neutral,
			GameTeam.Blue => WireSlotTeam.Blue,
			GameTeam.Red => WireSlotTeam.Red,
			_ => throw Unmapped(team.Value)
		};
	}

	/// <summary>Gets the team a client number stands for.</summary>
	/// <param name="team">The client's slot team.</param>
	/// <returns>The team, or <see langword="null" /> when the slot is on none or the number is unknown.</returns>
	public static GameTeam? FromWire(WireSlotTeam team)
	{
		return team switch
		{
			WireSlotTeam.Blue => GameTeam.Blue,
			WireSlotTeam.Red => GameTeam.Red,
			_ => null
		};
	}

	/// <summary>Gets the number the client uses for a team type.</summary>
	/// <param name="type">The team type.</param>
	/// <returns>The client's team type.</returns>
	public static WireTeamType ToWire(GameTeamType type)
	{
		return type switch
		{
			GameTeamType.HeadToHead => WireTeamType.HeadToHead,
			GameTeamType.TagCoop => WireTeamType.TagCoop,
			GameTeamType.TeamVs => WireTeamType.TeamVs,
			GameTeamType.TagTeamVs => WireTeamType.TagTeamVs,
			_ => throw Unmapped(type)
		};
	}

	/// <summary>Gets the team type a client number stands for.</summary>
	/// <param name="type">The client's team type.</param>
	/// <returns>The team type, or <see langword="null" /> when the number is not a team type.</returns>
	public static GameTeamType? FromWire(WireTeamType type)
	{
		return type switch
		{
			WireTeamType.HeadToHead => GameTeamType.HeadToHead,
			WireTeamType.TagCoop => GameTeamType.TagCoop,
			WireTeamType.TeamVs => GameTeamType.TeamVs,
			WireTeamType.TagTeamVs => GameTeamType.TagTeamVs,
			_ => null
		};
	}

	/// <summary>Gets the number the client uses for a win condition.</summary>
	/// <param name="condition">The win condition.</param>
	/// <returns>The client's win condition.</returns>
	public static WireWinCondition ToWire(GameWinCondition condition)
	{
		return condition switch
		{
			GameWinCondition.Score => WireWinCondition.Score,
			GameWinCondition.Accuracy => WireWinCondition.Accuracy,
			GameWinCondition.Combo => WireWinCondition.Combo,
			GameWinCondition.ScoreV2 => WireWinCondition.ScoreV2,
			_ => throw Unmapped(condition)
		};
	}

	/// <summary>Gets the win condition a client number stands for.</summary>
	/// <param name="condition">The client's win condition.</param>
	/// <returns>The win condition, or <see langword="null" /> when the number is not a win condition.</returns>
	public static GameWinCondition? FromWire(WireWinCondition condition)
	{
		return condition switch
		{
			WireWinCondition.Score => GameWinCondition.Score,
			WireWinCondition.Accuracy => GameWinCondition.Accuracy,
			WireWinCondition.Combo => GameWinCondition.Combo,
			WireWinCondition.ScoreV2 => GameWinCondition.ScoreV2,
			_ => null
		};
	}

	/// <summary>Gets the number the client uses for an activity.</summary>
	/// <param name="activity">The activity.</param>
	/// <returns>The client's activity.</returns>
	public static WireUserAction ToWire(UserActivity activity)
	{
		return activity switch
		{
			UserActivity.Idle => WireUserAction.Idle,
			UserActivity.Afk => WireUserAction.Afk,
			UserActivity.Playing => WireUserAction.Playing,
			UserActivity.Editing => WireUserAction.Editing,
			UserActivity.Modding => WireUserAction.Modding,
			UserActivity.Multiplayer => WireUserAction.Multiplayer,
			UserActivity.Watching => WireUserAction.Watching,
			UserActivity.Unknown => WireUserAction.Unknown,
			UserActivity.Testing => WireUserAction.Testing,
			UserActivity.Submitting => WireUserAction.Submitting,
			UserActivity.Paused => WireUserAction.Paused,
			UserActivity.Lobby => WireUserAction.Lobby,
			UserActivity.Multiplaying => WireUserAction.Multiplaying,
			UserActivity.OsuDirect => WireUserAction.OsuDirect,
			_ => throw Unmapped(activity)
		};
	}

	/// <summary>Gets the activity a client number stands for.</summary>
	/// <param name="action">The client's activity.</param>
	/// <returns>The activity, or <see langword="null" /> when the number is not an activity.</returns>
	public static UserActivity? FromWire(WireUserAction action)
	{
		return action switch
		{
			WireUserAction.Idle => UserActivity.Idle,
			WireUserAction.Afk => UserActivity.Afk,
			WireUserAction.Playing => UserActivity.Playing,
			WireUserAction.Editing => UserActivity.Editing,
			WireUserAction.Modding => UserActivity.Modding,
			WireUserAction.Multiplayer => UserActivity.Multiplayer,
			WireUserAction.Watching => UserActivity.Watching,
			WireUserAction.Unknown => UserActivity.Unknown,
			WireUserAction.Testing => UserActivity.Testing,
			WireUserAction.Submitting => UserActivity.Submitting,
			WireUserAction.Paused => UserActivity.Paused,
			WireUserAction.Lobby => UserActivity.Lobby,
			WireUserAction.Multiplaying => UserActivity.Multiplaying,
			WireUserAction.OsuDirect => UserActivity.OsuDirect,
			_ => null
		};
	}

	/// <summary>Gets the number the client uses for a beatmap's ranked status.</summary>
	/// <param name="status">The ranked status.</param>
	/// <returns>The client's ranked status.</returns>
	public static WireBeatmapStatus ToWire(BeatmapsetStatus status)
	{
		return status switch
		{
			BeatmapsetStatus.NotSubmitted => WireBeatmapStatus.NotSubmitted,
			BeatmapsetStatus.Pending => WireBeatmapStatus.Pending,
			BeatmapsetStatus.UpdateAvailable => WireBeatmapStatus.UpdateAvailable,
			BeatmapsetStatus.Ranked => WireBeatmapStatus.Ranked,
			BeatmapsetStatus.Approved => WireBeatmapStatus.Approved,
			BeatmapsetStatus.Qualified => WireBeatmapStatus.Qualified,
			BeatmapsetStatus.Loved => WireBeatmapStatus.Loved,
			_ => throw Unmapped(status)
		};
	}

	/// <summary>Gets the ranked status a client number stands for.</summary>
	/// <param name="status">The client's ranked status.</param>
	/// <returns>The ranked status, or <see langword="null" /> when the number is not a ranked status.</returns>
	public static BeatmapsetStatus? FromWire(WireBeatmapStatus status)
	{
		return status switch
		{
			WireBeatmapStatus.NotSubmitted => BeatmapsetStatus.NotSubmitted,
			WireBeatmapStatus.Pending => BeatmapsetStatus.Pending,
			WireBeatmapStatus.UpdateAvailable => BeatmapsetStatus.UpdateAvailable,
			WireBeatmapStatus.Ranked => BeatmapsetStatus.Ranked,
			WireBeatmapStatus.Approved => BeatmapsetStatus.Approved,
			WireBeatmapStatus.Qualified => BeatmapsetStatus.Qualified,
			WireBeatmapStatus.Loved => BeatmapsetStatus.Loved,
			_ => null
		};
	}

	/// <summary>Gets the number the client uses for the status of an occupied slot.</summary>
	/// <param name="status">The occupant's status.</param>
	/// <returns>The client's slot status.</returns>
	public static WireSlotStatus ToWire(RoomSlotStatus status)
	{
		return status switch
		{
			RoomSlotStatus.NoMap => WireSlotStatus.NoMap,
			RoomSlotStatus.NotReady => WireSlotStatus.NotReady,
			RoomSlotStatus.Ready => WireSlotStatus.Ready,
			RoomSlotStatus.Playing => WireSlotStatus.Playing,
			RoomSlotStatus.Complete => WireSlotStatus.Complete,
			_ => throw Unmapped(status)
		};
	}

	/// <summary>Gets the occupant status a client number stands for.</summary>
	/// <param name="status">The client's slot status.</param>
	/// <returns>The status, or <see langword="null" /> when the slot is empty or the number is unknown.</returns>
	public static RoomSlotStatus? FromWire(WireSlotStatus status)
	{
		return status switch
		{
			WireSlotStatus.NoMap => RoomSlotStatus.NoMap,
			WireSlotStatus.NotReady => RoomSlotStatus.NotReady,
			WireSlotStatus.Ready => RoomSlotStatus.Ready,
			WireSlotStatus.Playing => RoomSlotStatus.Playing,
			WireSlotStatus.Complete => RoomSlotStatus.Complete,
			_ => null
		};
	}

	/// <summary>Gets the mod bits the client uses for a set of mods.</summary>
	/// <param name="mods">The mods.</param>
	/// <returns>The client's mod bits.</returns>
	public static WireMods ToWire(GameMods mods)
	{
		var wire = WireMods.NoMod;
		foreach (var (domain, bit) in Mods)
			if ((mods & domain) != GameMods.NoMod)
				wire |= bit;

		return wire;
	}

	/// <summary>Gets the mods a client's mod bits stand for.</summary>
	/// <param name="mods">The client's mod bits.</param>
	/// <returns>The mods; a bit the client defines but Domain does not know is dropped.</returns>
	public static GameMods FromWire(WireMods mods)
	{
		var domain = GameMods.NoMod;
		foreach (var (mod, bit) in Mods)
			if ((mods & bit) != WireMods.NoMod)
				domain |= mod;

		return domain;
	}

	/// <summary>Gets the privilege bits the client uses for a set of roles.</summary>
	/// <param name="roles">The roles.</param>
	/// <returns>The client's privilege bits.</returns>
	public static WireClientPrivileges ToWire(UserPrivileges roles)
	{
		var wire = WireClientPrivileges.None;
		foreach (var (role, bit) in Privileges)
			if ((roles & role) != UserPrivileges.None)
				wire |= bit;

		return wire;
	}

	/// <summary>Gets the roles a client's privilege bits stand for.</summary>
	/// <param name="privileges">The client's privilege bits.</param>
	/// <returns>The roles; a bit Domain does not know is dropped.</returns>
	public static UserPrivileges FromWire(WireClientPrivileges privileges)
	{
		var roles = UserPrivileges.None;
		foreach (var (role, bit) in Privileges)
			if ((privileges & bit) != WireClientPrivileges.None)
				roles |= role;

		return roles;
	}

	/// <summary>Gets the release stream a client build's stream stands for.</summary>
	/// <param name="stream">The build's stream.</param>
	/// <returns>The release stream, or <see langword="null" /> when the stream is unknown.</returns>
	public static OsuVersionStream? FromWire(ClientStream stream)
	{
		return stream switch
		{
			ClientStream.Stable => OsuVersionStream.Stable,
			ClientStream.Beta => OsuVersionStream.Beta,
			ClientStream.CuttingEdge => OsuVersionStream.CuttingEdge,
			ClientStream.Tourney => OsuVersionStream.Tourney,
			ClientStream.Dev => OsuVersionStream.Dev,
			_ => null
		};
	}

	private static ArgumentOutOfRangeException Unmapped<T>(T value) where T : struct, Enum
	{
		return new ArgumentOutOfRangeException(nameof(value), value, "The value has no wire counterpart.");
	}
}