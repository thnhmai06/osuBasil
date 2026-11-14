using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Sets whether the account the sending connection belongs to accepts private messages only from friends.</summary>
internal sealed class SetPmPrivateHandler : IRequestHandler<SetPmPrivate, bool>
{
	/// <inheritdoc />
	public async ValueTask<bool> Handle(SetPmPrivate request, CancellationToken ct)
	{
		return await request.Actor.User.ScopeTo(request.Actor).Profile.SetPmPrivateAsync(request.PmPrivate);
	}
}