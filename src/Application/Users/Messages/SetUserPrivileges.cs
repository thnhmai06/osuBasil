using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserPrivileges(Session Actor, UserReference Target, UserPrivileges Privileges)
	: IRequest<UserOutcome>;