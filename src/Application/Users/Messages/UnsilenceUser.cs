using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record UnsilenceUser(Session Actor, UserReference Target, string? Reason)
	: IRequest<UserOutcome>;