using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetAwayMessageHandler : IRequestHandler<SetAwayMessage, bool>
{
	public async ValueTask<bool> Handle(SetAwayMessage request, CancellationToken ct)
	{
		return await request.Actor.User.ScopeTo(request.Actor).Profile.SetAwayAsync(request.Message) == UserOutcome.Ok;
	}
}