using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserPrivileges(Session Actor, int TargetId, UserPrivileges Privileges)
	: IRequest<UserActionResult>;