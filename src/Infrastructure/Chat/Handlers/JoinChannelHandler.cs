using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Common;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

/// <summary>Adds the connection that asked to the channel its target names.</summary>
internal sealed class JoinChannelHandler(ChannelDirectory directory)
	: IRequestHandler<JoinChannel, Result<ChannelOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<ChannelOutcome>> Handle(JoinChannel request, CancellationToken cancellationToken)
	{
		Result<ChannelOutcome> result = directory.Find(request.Target, request.Actor) is { } channel
			? channel.ScopeTo(request.Actor).Join()
			: ChannelOutcome.NotFound;
		return ValueTask.FromResult(result);
	}
}
