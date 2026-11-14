using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to replace the privileges granted to an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Privileges">The new privileges.</param>
public sealed record SetUserPrivileges(Connection Actor, UserReference Target, UserPrivileges Privileges)
	: IRequest<UserOutcome>;