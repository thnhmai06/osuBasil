using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record ListUserSessions(
	Connection Actor,
	UserReference Target,
	PageRequest Page,
	SessionState State,
	IReadOnlyList<SessionType>? Types) : IRequest<(UserOutcome Result, Page<SessionRecord>? Sessions)>;