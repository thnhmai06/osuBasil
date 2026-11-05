using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to close one session of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="SessionId">The session to close.</param>
public sealed record CloseUserSession(Connection Actor, UserRecord Target, Guid SessionId)
	: IRequest<Result<UserOutcome>>;