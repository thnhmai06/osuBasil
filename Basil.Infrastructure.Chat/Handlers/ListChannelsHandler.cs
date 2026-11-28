using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class ListChannelsHandler(GeneralChannelRegistry registry)
	: IRequestHandler<ListChannels, IReadOnlyList<GeneralChannelRecord>>
{
	public ValueTask<IReadOnlyList<GeneralChannelRecord>> Handle(ListChannels request, CancellationToken ct)
	{
		return ValueTask.FromResult<IReadOnlyList<GeneralChannelRecord>>([
			.. registry.VisibleTo(request.Actor).OrderBy(channel => channel.Name).Select(channel => channel.Record)
		]);
	}
}
