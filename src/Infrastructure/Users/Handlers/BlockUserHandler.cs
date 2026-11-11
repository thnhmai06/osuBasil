using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="BlockUser" /> on the account it names.</summary>
internal sealed class BlockUserHandler(UserRegistry registry) : IRequestHandler<BlockUser, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(BlockUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Relationships.BlockAsync();
	}
}