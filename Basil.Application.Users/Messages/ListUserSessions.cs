using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Mediator;
using Session = Basil.Application.Users.Objects.Sessions.Session;

namespace Basil.Application.Users.Messages;

public sealed record ListUserSessions(
	Session Actor,
	UserReference Target,
	PageRequest Page,
	SessionFilter State,
	IReadOnlyList<SessionType>? Types)
	: IRequest<(UserOutcome Result, Page<Domain.Authentication.Session>? Sessions)>;