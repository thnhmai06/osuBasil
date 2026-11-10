using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserAway" /> on the account it names.</summary>
internal sealed class SetUserAwayHandler(UserRegistry registry) : IRequestHandler<SetUserAway, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(SetUserAway request, CancellationToken ct)
	{
		return ValueTask.FromResult(registry.Of(request.Target).ScopeTo(request.Actor).Profile
			.SetAway(request.Message));
	}
}