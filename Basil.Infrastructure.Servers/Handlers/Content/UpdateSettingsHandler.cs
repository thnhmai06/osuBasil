using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class UpdateSettingsHandler(Server server) : IRequestHandler<UpdateSettings, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(UpdateSettings request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Settings.UpdateAsync(request.Settings);
	}
}