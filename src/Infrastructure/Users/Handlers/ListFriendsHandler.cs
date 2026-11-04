using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ListFriends" /> on the account it names.</summary>
internal sealed class ListFriendsHandler(UserRegistry registry) : IRequestHandler<ListFriends, (Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)> Handle(ListFriends request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Relationships.FriendsAsync(cancellationToken);
	}
}
