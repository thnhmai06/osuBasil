using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserPrivileges(IActor Actor, int TargetId, UserPrivileges Privileges)
	: IRequest<UserActionResult>;