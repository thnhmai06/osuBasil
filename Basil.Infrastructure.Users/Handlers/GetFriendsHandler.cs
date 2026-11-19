using Basil.Application.Users.Messages;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class GetFriendsHandler
	: IRequestHandler<GetFriends, (UserActionResult Result, IReadOnlyList<UserRecord> Users)>
{
	public async ValueTask<(UserActionResult Result, IReadOnlyList<UserRecord> Users)> Handle(GetFriends request,
		CancellationToken ct)
	{
		return await request.Actor.User.ScopeTo(request.Actor).Relationships.FriendsAsync(ct);
	}
}