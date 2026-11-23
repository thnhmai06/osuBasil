using Basil.Application.Users.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class ListFriendsHandler(UserRegistry registry)
	: IRequestHandler<ListFriends, (UserActionResult Result, IReadOnlyList<UserRecord> Users)>
{
	public async ValueTask<(UserActionResult Result, IReadOnlyList<UserRecord> Users)> Handle(ListFriends request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.TargetId).Relationships.FriendsAsync(ct);
	}
}