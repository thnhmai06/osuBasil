using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Users;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserPrivileges(Session Actor, UserReference Target, UserPrivileges Privileges)
	: IRequest<UserOutcome>;