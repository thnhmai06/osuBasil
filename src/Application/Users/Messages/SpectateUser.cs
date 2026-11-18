using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SpectateUser(Session Actor, StableSession Host) : IRequest<SessionOutcome>;