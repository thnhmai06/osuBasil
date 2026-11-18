using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class OpenIrcSessionHandler(UserRegistry registry)
	: IRequestHandler<OpenIrcSession, (UserOutcome Result, string? Key, SessionRecord? Session)>
{
	public async ValueTask<(UserOutcome Result, string? Key, SessionRecord? Session)> Handle(
		OpenIrcSession request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.OpenIrcAsync(request.Label);
	}
}