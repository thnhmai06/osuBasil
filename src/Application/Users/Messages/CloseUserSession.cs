using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record CloseUserSession(Connection Actor, UserReference Target, long SessionId)
	: IRequest<UserOutcome>;