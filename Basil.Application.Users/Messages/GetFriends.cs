using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record GetFriends(IActor Actor)
	: IRequest<(UserActionResult Result, IReadOnlyList<UserRecord> Users)>;