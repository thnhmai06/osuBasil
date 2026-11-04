using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Sets whether the account the sending connection belongs to accepts private messages only from friends.</summary>
internal sealed class SetPmPrivateHandler : IRequestHandler<SetPmPrivate, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(SetPmPrivate request, CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(request.Actor.User.ScopeTo(request.Actor).Profile.SetPmPrivate(request.PmPrivate));
	}
}
