using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record CloseUserSession(Session Actor, UserReference Target, long SessionId)
	: IRequest<UserActionResult>;