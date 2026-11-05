using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Lists the friends of the account the sending connection belongs to.</summary>
internal sealed class GetFriendsHandler
	: IRequestHandler<GetFriends, (Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)> Handle(GetFriends request,
		CancellationToken cancellationToken)
	{
		return await request.Actor.User.ScopeTo(request.Actor).Relationships.FriendsAsync(cancellationToken);
	}
}