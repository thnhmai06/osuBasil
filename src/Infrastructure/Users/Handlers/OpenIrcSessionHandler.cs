using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="OpenIrcSession" /> on the account it names.</summary>
internal sealed class OpenIrcSessionHandler(UserRegistry registry)
	: IRequestHandler<OpenIrcSession, (Result<UserOutcome> Result, string? Key, SessionRecord? Session)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<UserOutcome> Result, string? Key, SessionRecord? Session)> Handle(
		OpenIrcSession request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Sessions
			.OpenIrcAsync(request.Label, cancellationToken);
	}
}