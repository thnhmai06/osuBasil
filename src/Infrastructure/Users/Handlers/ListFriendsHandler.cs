using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class ListFriendsHandler(UserRegistry registry)
	: IRequestHandler<ListFriends, (UserOutcome Result, IReadOnlyList<UserRecord> Users)>
{
	public async ValueTask<(UserOutcome Result, IReadOnlyList<UserRecord> Users)> Handle(ListFriends request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Relationships.FriendsAsync(ct);
	}
}