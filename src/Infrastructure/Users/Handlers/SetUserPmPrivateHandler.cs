using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserPmPrivate" /> on the account it names.</summary>
internal sealed class SetUserPmPrivateHandler(UserRegistry registry) : IRequestHandler<SetUserPmPrivate, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(SetUserPmPrivate request, CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(registry.Of(request.Target).ScopeTo(request.Actor).Profile.SetPmPrivate(request.PmPrivate));
	}
}
