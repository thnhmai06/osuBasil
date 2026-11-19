using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class DeleteScreenshotHandler(Server server) : IRequestHandler<DeleteScreenshot, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(DeleteScreenshot request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Screenshots.DeleteAsync(request.Screenshot);
	}
}