using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PostChatMessageHandler(ChannelDirectory directory)
	: IRequestHandler<PostChatMessage, ChannelOutcome>
{
	public async ValueTask<ChannelOutcome> Handle(PostChatMessage request,
		CancellationToken ct)
	{
		if (directory.Find(request.Target, request.Actor) is not { } channel) return ChannelOutcome.NotFound;
		return await channel.ScopeTo(request.Actor).PostAsync(request.Text, request.Notice, ct);
	}
}