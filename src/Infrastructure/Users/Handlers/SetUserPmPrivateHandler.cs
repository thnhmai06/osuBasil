using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserPmPrivate" /> on the account it names.</summary>
internal sealed class SetUserPmPrivateHandler(UserRegistry registry) : IRequestHandler<SetUserPmPrivate, bool>
{
	/// <inheritdoc />
	public async ValueTask<bool> Handle(SetUserPmPrivate request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile.SetPmPrivateAsync(request.PmPrivate);
	}
}