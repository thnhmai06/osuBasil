using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record CloseUserSession(Session Actor, int TargetId, long SessionId)
	: IRequest<UserActionResult>;