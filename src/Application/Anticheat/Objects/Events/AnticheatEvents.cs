using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Objects.Events;

public abstract record AnticheatEvent : Event;

public sealed record AnticheatPlayerFlagged(StableConnection Player, OsuFlags Signs) : AnticheatEvent;