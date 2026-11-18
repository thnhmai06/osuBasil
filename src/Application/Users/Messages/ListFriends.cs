using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record ListFriends(Connection Actor, UserReference Target)
	: IRequest<(UserOutcome Result, IReadOnlyList<UserRecord> Users)>;