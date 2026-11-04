using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Common;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

/// <summary>Posts a chat message to the channel its target names, for the connection that sent it.</summary>
internal sealed class PostChatMessageHandler(
	GeneralChannelRegistry general,
	Lobby lobby,
	RoomChannels roomChannels,
	SpectatorChannels spectatorChannels,
	PmChannels pmChannels,
	UserRegistry registry) : IRequestHandler<PostChatMessage, Result<ChannelOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ChannelOutcome>> Handle(PostChatMessage request,
		CancellationToken cancellationToken)
	{
		if (Resolve(request.Target, request.Actor) is not { } channel) return ChannelOutcome.NotFound;
		return await channel.ScopeTo(request.Actor).PostAsync(request.Text, request.Notice, cancellationToken);
	}

	private Channel? Resolve(ChatTarget target, Connection actor)
	{
		switch (target)
		{
			case ChatTarget.General(var name):
				return general.Find(name) is { IsClosed: false } found ? found : null;
			case ChatTarget.Room(var id):
				return lobby.Find(id) is { IsClosed: false } room ? roomChannels.Of(room) : null;
			case ChatTarget.OwnRoom:
				return actor is StableConnection player && lobby.RoomOf(player) is { IsClosed: false } own
					? roomChannels.Of(own)
					: null;
			case ChatTarget.OwnSpectators:
				var host = actor is StableConnection { Spectators.Count: > 0 } self
					? self
					: registry.FindSpectating(actor);
				return host is null ? null : spectatorChannels.Of(host);
			case ChatTarget.Private(var recipient):
				return registry.Online.TryGetValue(recipient, out var user) ? pmChannels.Of(user) : null;
			default:
				return null;
		}
	}
}
