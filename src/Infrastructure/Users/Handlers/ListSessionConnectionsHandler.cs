using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ListSessionConnections" /> on the account it names.</summary>
internal sealed class ListSessionConnectionsHandler(UserRegistry registry)
	: IRequestHandler<ListSessionConnections, (UserOutcome Result, Page<ConnectionRecord>? Connections)>
{
	/// <inheritdoc />
	public async ValueTask<(UserOutcome Result, Page<ConnectionRecord>? Connections)> Handle(
		ListSessionConnections request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Sessions
			.ListConnectionsAsync(request.SessionId, request.Page, ct);
	}
}
