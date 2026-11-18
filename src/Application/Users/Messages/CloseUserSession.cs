using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record CloseUserSession(Session Actor, UserReference Target, long SessionId)
	: IRequest<UserOutcome>;