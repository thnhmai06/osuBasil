using Basil.Application.Sessions.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Mediator;
using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Application.Sessions.Messages;

public sealed record ListUserSessions(
	Session Actor,
	int TargetId,
	PageRequest Page,
	SessionFilter State,
	IReadOnlyList<SessionType>? Types)
	: IRequest<(UserActionResult Result, Page<SessionRecord>? Sessions)>;