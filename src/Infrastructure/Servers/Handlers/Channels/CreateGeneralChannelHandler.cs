using Basil.Application.Common;
using Basil.Application.Servers.Messages.Channels;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Channels;

/// <summary>Creates a general channel for the connection that asked.</summary>
internal sealed class CreateGeneralChannelHandler(Server server)
	: IRequestHandler<CreateGeneralChannel, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<ServerOutcome>> Handle(CreateGeneralChannel request, CancellationToken cancellationToken)
	{
		return new ValueTask<Result<ServerOutcome>>(
			server.ScopeTo(request.Actor).Channels.CreateAsync(request.Record, cancellationToken));
	}
}