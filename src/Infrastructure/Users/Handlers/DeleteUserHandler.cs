using Basil.Application.Shared;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="DeleteUser" /> on the account it names.</summary>
internal sealed class DeleteUserHandler(UserRegistry registry) : IRequestHandler<DeleteUser, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(DeleteUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).DeleteAsync();
	}
}