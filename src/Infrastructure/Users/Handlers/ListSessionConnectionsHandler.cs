using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ListSessionConnections" /> on the account it names.</summary>
internal sealed class ListSessionConnectionsHandler(UserRegistry registry)
	: IRequestHandler<ListSessionConnections, (Result<UserOutcome> Result, Page<ConnectionRecord>? Connections)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<UserOutcome> Result, Page<ConnectionRecord>? Connections)> Handle(
		ListSessionConnections request, CancellationToken ct)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Sessions
			.ListConnectionsAsync(request.SessionId, request.Page, ct);
	}
}
