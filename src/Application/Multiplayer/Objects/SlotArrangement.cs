using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects;

public sealed record SlotArrangement(int Index, UserRecord? Player, GameTeam? Team, bool Locked);