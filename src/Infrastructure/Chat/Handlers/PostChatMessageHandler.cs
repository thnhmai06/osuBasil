using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Common;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

/// <summary>Posts a chat message to the channel its target names, for the connection that sent it.</summary>
internal sealed class PostChatMessageHandler(ChannelDirectory directory)
	: IRequestHandler<PostChatMessage, Result<ChannelOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ChannelOutcome>> Handle(PostChatMessage request,
		CancellationToken cancellationToken)
	{
		if (directory.Find(request.Target, request.Actor) is not { } channel) return ChannelOutcome.NotFound;
		return await channel.ScopeTo(request.Actor).PostAsync(request.Text, request.Notice, cancellationToken);
	}
}
