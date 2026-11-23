using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class GetFriendsHandler(UserRegistry registry)
	: IRequestHandler<GetFriends, (UserActionResult Result, IReadOnlyList<UserRecord> Users)>
{
	public async ValueTask<(UserActionResult Result, IReadOnlyList<UserRecord> Users)> Handle(GetFriends request,
		CancellationToken ct)
	{
		return await registry.Of(request.Actor.Record).ScopeTo(request.Actor).Relationships.FriendsAsync(ct);
	}
}