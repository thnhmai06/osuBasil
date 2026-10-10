using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Multiplayer.Match;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Basil;

/// <summary>What the bot knows about an open room.</summary>
/// <param name="Id">The room id.</param>
/// <param name="Name">The room name.</param>
/// <param name="Channel">The room's chat channel, such as <c>#mp_5</c>.</param>
/// <param name="Creator">The user who created the room.</param>
/// <param name="Referees">The room's referees, the creator not included.</param>
/// <param name="Banned">The users banned from the room.</param>
/// <param name="Host">The current host, or <see langword="null" />.</param>
/// <param name="Settings">The game settings: mode, mods, freemods, team type, win condition.</param>
/// <param name="Beatmap">The selected beatmap, or <see langword="null" />.</param>
/// <param name="HasPassword">Whether the room has a password.</param>
/// <param name="Locked">Whether players cannot move themselves between slots.</param>
/// <param name="IsPrivate">Whether the room's match history is private.</param>
/// <param name="InProgress">Whether a round is being played.</param>
/// <param name="Slots">All 16 slots, numbered 1 to 16.</param>
public sealed record RoomState(
	int Id,
	string Name,
	string Channel,
	User? Creator,
	IReadOnlyList<User> Referees,
	IReadOnlyList<User> Banned,
	User? Host,
	MatchSettings Settings,
	RoomBeatmap? Beatmap,
	bool HasPassword,
	bool Locked,
	bool IsPrivate,
	bool InProgress,
	IReadOnlyList<RoomSlotState> Slots);

/// <summary>The beatmap selected in a room.</summary>
/// <param name="Id">The beatmap id.</param>
/// <param name="Name">The display name, "Artist - Title [Version]".</param>
/// <param name="Mode">The beatmap's game mode.</param>
public sealed record RoomBeatmap(int Id, string Name, GameMode Mode);

/// <summary>One slot of a room.</summary>
/// <param name="Index">The slot number, 1 to 16.</param>
/// <param name="Player">The seated user, or <see langword="null" /> when empty.</param>
/// <param name="Team">The player's team, or <see langword="null" />.</param>
/// <param name="Status">The player's status, or <see langword="null" /> when empty.</param>
/// <param name="Mods">The player's own mods under freemod, or <see langword="null" />.</param>
/// <param name="Locked">Whether the slot is locked.</param>
public sealed record RoomSlotState(
	int Index,
	User? Player,
	GameTeam? Team,
	RoomSlotStatus? Status,
	GameMods? Mods,
	bool Locked);

/// <summary>The status of a seated player.</summary>
public enum RoomSlotStatus : byte
{
	/// <summary>The player does not have the beatmap.</summary>
	NoMap,

	/// <summary>The player is not ready.</summary>
	NotReady,

	/// <summary>The player is ready.</summary>
	Ready,

	/// <summary>The player is playing the round.</summary>
	Playing,

	/// <summary>The player finished the round.</summary>
	Complete
}