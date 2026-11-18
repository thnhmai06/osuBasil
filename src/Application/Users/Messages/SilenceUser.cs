using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SilenceUser(Connection Actor, UserReference Target, DateTimeOffset EndsAt, string? Reason)
	: IRequest<UserOutcome>;