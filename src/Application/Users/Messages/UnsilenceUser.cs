using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record UnsilenceUser(Connection Actor, UserReference Target, string? Reason)
	: IRequest<UserOutcome>;