using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

internal sealed class DeleteScreenshotHandler(Server server) : IRequestHandler<DeleteScreenshot, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(DeleteScreenshot request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Screenshots.DeleteAsync(request.Screenshot);
	}
}