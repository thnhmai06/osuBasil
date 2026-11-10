using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ListUserSessions" /> on the account it names.</summary>
internal sealed class ListUserSessionsHandler(UserRegistry registry)
	: IRequestHandler<ListUserSessions, (Result<UserOutcome> Result, Page<SessionRecord>? Sessions)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<UserOutcome> Result, Page<SessionRecord>? Sessions)> Handle(ListUserSessions request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.ListAsync(request.Page, request.State, request.Types, ct);
	}
}