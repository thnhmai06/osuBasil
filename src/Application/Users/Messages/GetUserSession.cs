using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Session = Basil.Application.Users.Objects.Sessions.Session;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record GetUserSession(Session Actor, UserReference Target, long SessionId)
	: IRequest<(UserOutcome Result, Basil.Domain.Authentication.Session? Session)>;