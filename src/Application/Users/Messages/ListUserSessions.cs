using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Session = Basil.Application.Users.Objects.Sessions.Session;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record ListUserSessions(
	Session Actor,
	UserReference Target,
	PageRequest Page,
	SessionFilter State,
	IReadOnlyList<SessionType>? Types)
	: IRequest<(UserOutcome Result, Page<Basil.Domain.Authentication.Session>? Sessions)>;