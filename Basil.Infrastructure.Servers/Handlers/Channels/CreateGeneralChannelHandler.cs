using Basil.Application.Servers.Messages.Channels;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Channels;

internal sealed class CreateGeneralChannelHandler(Server server)
	: IRequestHandler<CreateGeneralChannel, ServerActionResult>
{
	public ValueTask<ServerActionResult> Handle(CreateGeneralChannel request, CancellationToken ct)
	{
		return new ValueTask<ServerActionResult>(
			server.ScopeTo(request.Actor).Channels.CreateAsync(request.Record));
	}
}