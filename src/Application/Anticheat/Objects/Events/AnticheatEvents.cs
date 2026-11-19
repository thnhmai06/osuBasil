using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Objects.Events;

public abstract record AnticheatEvent : Event;

public sealed record AnticheatPlayerFlagged(StableSession Player, OsuFlags Signs) : AnticheatEvent;