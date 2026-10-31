using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Mediator;
using Basil.Application.Chat.Registry;

namespace Basil.Application.Chat.Handlers;

/// <summary>Takes a left spectator and, once none remain, the spectated client out of the client's spectator channel.</summary>
internal sealed class SpectatorLeftHandler(SpectatorChannels channels) : INotificationHandler<UserSpectatorLeft>
{
	/// <inheritdoc />
	public ValueTask Handle(UserSpectatorLeft @event, CancellationToken cancellationToken)
	{
		if (@event.Spectator is ApiConnection || channels.Find(@event.Host) is not { } channel)
			return ValueTask.CompletedTask;

		channel.ScopeTo(@event.Spectator).Part();
		if (@event.Host.Spectators.All(spectator => spectator is ApiConnection))
			channel.ScopeTo(@event.Host).Part();

		return ValueTask.CompletedTask;
	}
}