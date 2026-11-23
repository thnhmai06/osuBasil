using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Mediator;
using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Application.Sessions.Messages;

public sealed record GetUserSession(Session Actor, int TargetId, long SessionId)
	: IRequest<(UserActionResult Result, SessionRecord? Session)>;