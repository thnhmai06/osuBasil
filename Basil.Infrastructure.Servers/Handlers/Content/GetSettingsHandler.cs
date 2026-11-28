using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class GetSettingsHandler(Server server)
	: IRequestHandler<GetSettings, (ServerActionResult Result, ServerSettings? Settings)>
{
	public async ValueTask<(ServerActionResult Result, ServerSettings? Settings)> Handle(GetSettings request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Settings.GetAsync(ct);
	}
}