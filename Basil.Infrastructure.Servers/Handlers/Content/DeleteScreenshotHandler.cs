using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class DeleteScreenshotHandler(Server server) : IRequestHandler<DeleteScreenshot, ServerActionResult>
{
	public async ValueTask<ServerActionResult> Handle(DeleteScreenshot request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Screenshots.DeleteAsync(request.Name);
	}
}