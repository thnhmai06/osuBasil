using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class JoinChannelHandler(ChannelDirectory directory)
	: IRequestHandler<JoinChannel, ChannelOutcome>
{
	public ValueTask<ChannelOutcome> Handle(JoinChannel request, CancellationToken ct)
	{
		var result = directory.Find(request.Target, request.Actor) is { } channel
			? channel.ScopeTo(request.Actor).Join()
			: ChannelOutcome.NotFound;
		return ValueTask.FromResult(result);
	}
}