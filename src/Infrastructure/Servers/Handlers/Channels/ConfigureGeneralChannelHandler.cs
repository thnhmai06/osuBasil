using Basil.Application.Servers.Messages.Channels;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Channels;

internal sealed class ConfigureGeneralChannelHandler(Server server)
	: IRequestHandler<ConfigureGeneralChannel, GeneralChannelAnswer>
{
	public async ValueTask<GeneralChannelAnswer> Handle(ConfigureGeneralChannel request,
		CancellationToken ct)
	{
		var (found, channel) = server.ScopeTo(request.Actor).Channels.FindGeneral(request.Name);
		if (channel is null) return new GeneralChannelAnswer(found, null);

		var result = await channel.ScopeTo(request.Actor).General!.Value.ConfigureAsync(request.Change);
		return new GeneralChannelAnswer(result, channel);
	}
}