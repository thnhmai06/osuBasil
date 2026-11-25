using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class UpdateSettingsHandler(Server server)
	: IRequestHandler<UpdateSettings, (ServerActionResult Result, ServerSettings? Settings)>
{
	public async ValueTask<(ServerActionResult Result, ServerSettings? Settings)> Handle(UpdateSettings request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Settings.UpdateAsync(request.Change);
	}
}