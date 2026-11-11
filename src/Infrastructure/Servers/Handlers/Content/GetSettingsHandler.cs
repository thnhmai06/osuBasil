using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="GetSettings" /> with the privileges of the sending connection.</summary>
internal sealed class GetSettingsHandler(Server server)
	: IRequestHandler<GetSettings, (ServerOutcome Result, ServerSettings? Settings)>
{
	/// <inheritdoc />
	public async ValueTask<(ServerOutcome Result, ServerSettings? Settings)> Handle(GetSettings request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Settings.GetAsync(ct);
	}
}