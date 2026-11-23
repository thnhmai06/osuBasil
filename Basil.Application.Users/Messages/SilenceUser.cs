using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SilenceUser(IActor Actor, int TargetId, DateTimeOffset EndsAt, string? Reason)
	: IRequest<UserActionResult>;