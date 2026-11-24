using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class MaySilenceUserHandler(UserRegistry registry) : IRequestHandler<MaySilenceUser, UserActionResult>
{
	public ValueTask<UserActionResult> Handle(MaySilenceUser request, CancellationToken ct)
	{
		return ValueTask.FromResult(registry.ScopeTo(request.Actor, request.TargetId).Silence.MayImpose());
	}
}
