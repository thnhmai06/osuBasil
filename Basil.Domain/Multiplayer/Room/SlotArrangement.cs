using Basil.Domain.Game;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>What one slot holds in a requested rearrangement of the room's slots.</summary>
/// <param name="Index">The 1-based index of the slot.</param>
/// <param name="Player">The player to seat there, or <see langword="null"/> for an empty slot.</param>
/// <param name="Team">The player's team; applied only in a team mode.</param>
/// <param name="Locked">Whether the slot is locked; a slot with a player cannot be locked.</param>
public sealed record SlotArrangement(int Index, UserRecord? Player, GameTeam? Team, bool Locked);