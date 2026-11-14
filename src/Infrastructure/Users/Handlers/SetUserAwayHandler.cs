using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserAway" /> on the account it names.</summary>
internal sealed class SetUserAwayHandler(UserRegistry registry) : IRequestHandler<SetUserAway, bool>
{
	/// <inheritdoc />
	public async ValueTask<bool> Handle(SetUserAway request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile.SetAwayAsync(request.Message);
	}
}