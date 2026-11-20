using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SilenceUser(Session Actor, int TargetId, DateTimeOffset EndsAt, string? Reason)
	: IRequest<UserActionResult>;