using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetAwayMessageHandler(UserRegistry registry) : IRequestHandler<SetAwayMessage, bool>
{
	public async ValueTask<bool> Handle(SetAwayMessage request, CancellationToken ct)
	{
		return await registry.Of(request.Actor.Record).ScopeTo(request.Actor).Profile.SetAwayAsync(request.Message) == UserActionResult.Ok;
	}
}