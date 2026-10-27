using Basil.Application.Users.Users;
using Mediator;

namespace Basil.Application.Chat.Runtime;

/// <summary>Takes a left spectator and, once none remain, the spectated client out of the client's spectator channel.</summary>
internal sealed class SpectatorLeftHandler(SpectatorChannels channels) : INotificationHandler<UserSpectatorLeft>
{
	/// <inheritdoc />
	public ValueTask Handle(UserSpectatorLeft @event, CancellationToken cancellationToken)
	{
		if (channels.Find(@event.Host) is not { } channel) return ValueTask.CompletedTask;

		channel.ScopeTo(@event.Spectator).Part();
		if (@event.Host.Spectators.Count == 0)
			channel.ScopeTo(@event.Host).Part();

		return ValueTask.CompletedTask;
	}
}