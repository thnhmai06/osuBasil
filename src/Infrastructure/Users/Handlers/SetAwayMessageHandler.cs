using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Sets the away message of the account the sending connection belongs to.</summary>
internal sealed class SetAwayMessageHandler : IRequestHandler<SetAwayMessage, bool>
{
	/// <inheritdoc />
	public async ValueTask<bool> Handle(SetAwayMessage request, CancellationToken ct)
	{
		return await request.Actor.User.ScopeTo(request.Actor).Profile.SetAwayAsync(request.Message);
	}
}