using Basil.Domain.Game.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer;

public sealed record SlotArrangement(int Index, UserRecord? Player, GameTeam? Team, bool Locked);