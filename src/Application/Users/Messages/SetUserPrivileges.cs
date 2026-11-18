using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserPrivileges(Connection Actor, UserReference Target, UserPrivileges Privileges)
	: IRequest<UserOutcome>;