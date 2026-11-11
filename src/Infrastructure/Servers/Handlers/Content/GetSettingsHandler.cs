using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="GetSettings" /> with the privileges of the sending connection.</summary>
internal sealed class GetSettingsHandler(Server server)
	: IRequestHandler<GetSettings, (Result<ServerOutcome> Result, ServerSettings? Settings)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<ServerOutcome> Result, ServerSettings? Settings)> Handle(GetSettings request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Settings.GetAsync(ct);
	}
}