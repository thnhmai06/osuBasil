using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Sessions.Messages;

public sealed record CloseUserSession(Session Actor, int TargetId, long SessionId)
	: IRequest<UserActionResult>;