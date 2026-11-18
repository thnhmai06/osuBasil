using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SilenceUserHandler(UserRegistry registry) : IRequestHandler<SilenceUser, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(SilenceUser request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Silence
			.ImposeAsync(request.EndsAt, request.Reason);
	}
}