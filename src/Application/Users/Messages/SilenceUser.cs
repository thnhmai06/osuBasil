using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to silence an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="EndsAt">When the silence ends.</param>
/// <param name="Reason">Why the account is silenced, if known.</param>
public sealed record SilenceUser(Connection Actor, UserRecord Target, DateTimeOffset EndsAt, string? Reason)
	: IRequest<Result<UserOutcome>>;
