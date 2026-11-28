using Basil.Application.Sessions.Objects;
using Basil.Domain.Game;
using Mediator;

namespace Basil.Application.Sessions.Messages;

public sealed record SetPresenceFilter(StableSession Actor, PresenceFilter Filter) : IRequest<bool>;
