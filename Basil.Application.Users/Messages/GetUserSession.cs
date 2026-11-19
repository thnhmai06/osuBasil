using Basil.Domain.Users;
using Mediator;
using Session = Basil.Application.Users.Objects.Sessions.Session;

namespace Basil.Application.Users.Messages;

public sealed record GetUserSession(Session Actor, UserReference Target, long SessionId)
	: IRequest<(UserOutcome Result, Domain.Authentication.Session? Session)>;