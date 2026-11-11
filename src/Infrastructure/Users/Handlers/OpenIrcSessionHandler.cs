using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="OpenIrcSession" /> on the account it names.</summary>
internal sealed class OpenIrcSessionHandler(UserRegistry registry)
	: IRequestHandler<OpenIrcSession, (UserOutcome Result, string? Key, SessionRecord? Session)>
{
	/// <inheritdoc />
	public async ValueTask<(UserOutcome Result, string? Key, SessionRecord? Session)> Handle(
		OpenIrcSession request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.OpenIrcAsync(request.Label);
	}
}