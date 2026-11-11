using Basil.Application.Shared;
using Basil.Application.Servers.Messages.Channels;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Channels;

/// <summary>Creates a general channel for the connection that asked.</summary>
internal sealed class CreateGeneralChannelHandler(Server server)
	: IRequestHandler<CreateGeneralChannel, ServerOutcome>
{
	/// <inheritdoc />
	public ValueTask<ServerOutcome> Handle(CreateGeneralChannel request, CancellationToken ct)
	{
		return new ValueTask<ServerOutcome>(
			server.ScopeTo(request.Actor).Channels.CreateAsync(request.Record));
	}
}