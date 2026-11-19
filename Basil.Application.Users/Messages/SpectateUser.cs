using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Authentication;
using Mediator;
using Session = Basil.Application.Users.Objects.Sessions.Session;

namespace Basil.Application.Users.Messages;

public sealed record SpectateUser(Session Actor, StableSession Host) : IRequest<SessionAuthResult>;