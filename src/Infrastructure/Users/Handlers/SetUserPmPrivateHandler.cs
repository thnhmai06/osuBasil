using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserPmPrivate" /> on the account it names.</summary>
internal sealed class SetUserPmPrivateHandler(UserRegistry registry) : IRequestHandler<SetUserPmPrivate, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(SetUserPmPrivate request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile.SetPmPrivateAsync(request.PmPrivate);
	}
}