using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record UnsilenceUser(IActor Actor, int TargetId, string? Reason)
	: IRequest<UserActionResult>;