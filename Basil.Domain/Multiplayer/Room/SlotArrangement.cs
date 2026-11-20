using Basil.Domain.Game;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

public sealed record SlotArrangement(int Index, UserRecord? Player, GameTeam? Team, bool Locked);