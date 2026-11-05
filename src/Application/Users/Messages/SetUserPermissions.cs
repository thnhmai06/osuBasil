using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to replace the permissions granted to an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Permissions">The new permissions.</param>
public sealed record SetUserPermissions(Connection Actor, UserRecord Target, UserPermissions Permissions)
	: IRequest<Result<UserOutcome>>;