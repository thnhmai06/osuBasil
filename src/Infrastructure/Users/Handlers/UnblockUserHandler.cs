using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="UnblockUser" /> on the account it names.</summary>
internal sealed class UnblockUserHandler(UserRegistry registry) : IRequestHandler<UnblockUser, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(UnblockUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Relationships.UnblockAsync();
	}
}