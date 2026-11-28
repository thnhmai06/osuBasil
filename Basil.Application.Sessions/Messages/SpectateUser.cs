using Basil.Application.Sessions.Objects;
using Mediator;
using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Application.Sessions.Messages;

public sealed record SpectateUser(Session Actor, StableSession Host) : IRequest<SessionAuthResult>;