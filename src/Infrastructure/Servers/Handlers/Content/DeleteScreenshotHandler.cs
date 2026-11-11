using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="DeleteScreenshot" /> with the privileges of the sending connection.</summary>
internal sealed class DeleteScreenshotHandler(Server server) : IRequestHandler<DeleteScreenshot, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ServerOutcome>> Handle(DeleteScreenshot request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Screenshots.DeleteAsync(request.Screenshot);
	}
}