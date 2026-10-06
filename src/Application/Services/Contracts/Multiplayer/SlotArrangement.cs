using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>What one slot holds after the slots of a room are arranged.</summary>
/// <param name="Index">The slot number, from 1 to 16.</param>
/// <param name="Player">The user seated in the slot, or <see langword="null" /> to leave it empty.</param>
/// <param name="Team">The team of the seated user, or <see langword="null" /> to keep their team.</param>
/// <param name="Locked">Whether the slot is locked; a slot with a player cannot be locked.</param>
public sealed record SlotArrangement(int Index, User? Player, GameTeam? Team, bool Locked);