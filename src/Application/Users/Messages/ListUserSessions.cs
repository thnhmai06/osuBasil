using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to list the sessions of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Page">The page to read.</param>
/// <param name="State">Which sessions to list.</param>
/// <param name="Types">The kinds of session to list, or null for all kinds.</param>
public sealed record ListUserSessions(
	Connection Actor,
	UserReference Target,
	PageRequest Page,
	SessionState State,
	IReadOnlyList<SessionType>? Types) : IRequest<(Result<UserOutcome> Result, Page<SessionRecord>? Sessions)>;