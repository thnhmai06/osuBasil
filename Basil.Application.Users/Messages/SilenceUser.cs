using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SilenceUser(Session Actor, UserReference Target, DateTimeOffset EndsAt, string? Reason)
	: IRequest<UserActionResult>;