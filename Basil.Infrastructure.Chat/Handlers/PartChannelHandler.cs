using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PartChannelHandler(ChannelDirectory directory)
	: IRequestHandler<PartChannel, ChannelJoinResult>
{
	public ValueTask<ChannelJoinResult> Handle(PartChannel request, CancellationToken ct)
	{
		var result = directory.Find(request.Target, request.Actor) is { } channel
			? channel.ScopeTo(request.Actor).Part()
			: ChannelJoinResult.NotFound;
		return ValueTask.FromResult(result);
	}
}