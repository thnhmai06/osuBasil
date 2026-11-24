using Basil.Application.Shared.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Objects.Events;

public abstract record AnticheatEvent : Event;

public sealed record AnticheatPlayerFlagged(StableSession Player, ClientFlags Signs) : AnticheatEvent;