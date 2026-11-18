using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record CloseUserSessions(Connection Actor, UserReference Target, bool KeepCurrent)
	: IRequest<UserOutcome>;