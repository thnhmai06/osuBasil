using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class GetChannelPeersHandler(ChannelDirectory directory)
	: IRequestHandler<GetChannelPeers, IReadOnlyList<Session>>
{
	public ValueTask<IReadOnlyList<Session>> Handle(GetChannelPeers request, CancellationToken ct)
	{
		return ValueTask.FromResult(directory.PeersOf(request.Actor));
	}
}