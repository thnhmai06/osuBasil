using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record ListBlocks(IActor Actor, int TargetId)
	: IRequest<(UserActionResult Result, IReadOnlyList<UserRecord> Users)>;