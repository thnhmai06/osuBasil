using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PartChannelHandler(ChannelDirectory directory)
	: IRequestHandler<PartChannel, ChannelOutcome>
{
	public ValueTask<ChannelOutcome> Handle(PartChannel request, CancellationToken ct)
	{
		var result = directory.Find(request.Target, request.Actor) is { } channel
			? channel.ScopeTo(request.Actor).Part()
			: ChannelOutcome.NotFound;
		return ValueTask.FromResult(result);
	}
}