using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Domain.Chat;
using Mediator;
using Basil.Application.Chat.Objects;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PostChatMessageHandler(ChannelDirectory directory)
	: IRequestHandler<PostChatMessage, ChannelJoinResult>
{
	public async ValueTask<ChannelJoinResult> Handle(PostChatMessage request,
		CancellationToken ct)
	{
		if (directory.Find(request.Target, request.Actor) is not { } channel) return ChannelJoinResult.NotFound;
		return await channel.ScopeTo(request.Actor).PostAsync(request.Text, request.Notice, ct);
	}
}