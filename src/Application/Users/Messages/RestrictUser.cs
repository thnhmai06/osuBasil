using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to restrict an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Permissions">The permissions to take away.</param>
/// <param name="EndsAt">When the restriction ends, or null for never.</param>
/// <param name="Reason">Why the account is restricted, if known.</param>
public sealed record RestrictUser(Connection Actor, UserRecord Target, UserPermissions Permissions, DateTimeOffset? EndsAt, string? Reason) : IRequest<(Result<UserOutcome> Result, UserRestriction? Restriction)>;
