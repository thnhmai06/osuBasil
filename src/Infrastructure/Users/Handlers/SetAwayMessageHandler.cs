using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Sets the away message of the account the sending connection belongs to.</summary>
internal sealed class SetAwayMessageHandler : IRequestHandler<SetAwayMessage, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(SetAwayMessage request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Actor.User.ScopeTo(request.Actor).Profile.SetAway(request.Message));
	}
}