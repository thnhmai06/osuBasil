using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Registries;
using Basil.Application.Models.Notifications;
using Basil.Application.Models.Sessions;

namespace Basil.Application.Services.EventHandlers;

/// <summary>
///     Reacts to a session's presence and spectating events: a reported status change, and starting
///     or stopping spectating another client.
/// </summary>
public sealed class SessionEventHandlers(ISessionRegistry<GameSession> games) :
	IEventHandler<StatusChanged>,
	IEventHandler<SpectateStarted>,
	IEventHandler<SpectateStopped>
{
	/// <inheritdoc />
	public Task HandleAsync(SpectateStarted domainEvent, CancellationToken cancellationToken = default)
	{
		domainEvent.Host.Notify(new SpectatorJoined(domainEvent.Spectator.User.Id));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(SpectateStopped domainEvent, CancellationToken cancellationToken = default)
	{
		domainEvent.Host.Notify(new SpectatorLeft(domainEvent.Spectator.User.Id));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(StatusChanged domainEvent, CancellationToken cancellationToken = default)
	{
		var notification = new PresenceChanged(domainEvent.Session);
		foreach (var session in games.AllByUser.Values)
			session.Notify(notification);
		return Task.CompletedTask;
	}
}
