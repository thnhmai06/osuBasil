using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record GetFriends(Session Actor)
	: IRequest<(UserActionResult Result, IReadOnlyList<UserRecord> Users)>;