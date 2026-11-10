using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="AddFriend" /> on the account it names.</summary>
internal sealed class AddFriendHandler(UserRegistry registry) : IRequestHandler<AddFriend, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(AddFriend request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Relationships.AddFriendAsync();
	}
}