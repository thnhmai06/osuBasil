using Basil.Application.Common.Events;
using Basil.Application.Common.Notifications;
namespace Basil.Application.Sessions;

/// <summary>
///     Reacts to a session's presence and spectating events: a reported status change, and another
///     session starting or stopping spectating this one.
/// </summary>
public sealed class SessionEventHandlers(ISessionRegistry<GameSession> games) :
	IEventHandler<PresenceEvent>,
	IEventHandler<SpectatorEvent>
{
	/// <inheritdoc />
	public Task HandleAsync(SpectatorEvent domainEvent, CancellationToken cancellationToken = default)
	{
		switch (domainEvent)
		{
			case SpectatorAdded added:
				added.HostSession.Notify(new SpectatorJoined(added.Spectator));
				break;
			case SpectatorRemoved removed:
				removed.HostSession.Notify(new SpectatorLeft(removed.Spectator));
				break;
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(PresenceEvent domainEvent, CancellationToken cancellationToken = default)
	{
		if (domainEvent is StatusChanged statusChanged)
		{
			var notification = new PresenceChanged(statusChanged.GameSession);
			foreach (var session in games.AllByUser.Values)
				session.Notify(notification);
		}

		return Task.CompletedTask;
	}
}
