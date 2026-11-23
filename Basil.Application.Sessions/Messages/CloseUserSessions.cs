using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Sessions.Messages;

public sealed record CloseUserSessions(Session Actor, int TargetId, bool KeepCurrent)
	: IRequest<UserActionResult>;