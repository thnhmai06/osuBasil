using Basil.Application.Servers.Messages.Channels;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Channels;

/// <summary>Deletes a general channel for the connection that asked.</summary>
internal sealed class DeleteGeneralChannelHandler(Server server)
	: IRequestHandler<DeleteGeneralChannel, GeneralChannelAnswer>
{
	/// <inheritdoc />
	public async ValueTask<GeneralChannelAnswer> Handle(DeleteGeneralChannel request,
		CancellationToken ct)
	{
		var (found, channel) = server.ScopeTo(request.Actor).Channels.Find(request.Name);
		if (channel is null) return new GeneralChannelAnswer(found, null);

		var result = await channel.ScopeTo(request.Actor).General!.Value.DeleteAsync();
		return new GeneralChannelAnswer(result, channel);
	}
}