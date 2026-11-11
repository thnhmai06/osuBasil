using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="UploadScreenshot" /> with the privileges of the sending connection.</summary>
internal sealed class UploadScreenshotHandler(Server server)
	: IRequestHandler<UploadScreenshot, (ContentReference? Screenshot, ServerOutcome Result)>
{
	/// <inheritdoc />
	public async ValueTask<(ContentReference? Screenshot, ServerOutcome Result)> Handle(
		UploadScreenshot request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Screenshots.UploadAsync(request.Image, ct);
	}
}