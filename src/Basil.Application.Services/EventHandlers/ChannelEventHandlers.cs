using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Registries;
using Basil.Application.Models.Sessions;
using Notification = Basil.Application.Models.Notifications;

namespace Basil.Application.Services.EventHandlers;

/// <summary>Reacts to a channel's membership events: a session joining or parting.</summary>
public sealed class ChannelEventHandlers(IPlayerRegistry players) :
	IDomainEventHandler<ChannelJoined>,
	IDomainEventHandler<ChannelParted>
{
	/// <inheritdoc />
	public Task HandleAsync(ChannelJoined domainEvent, CancellationToken cancellationToken = default)
	{
		domainEvent.Session.Notify(new Notification.ChannelJoined(domainEvent.Channel.Name));
		NotifyOtherMembers(domainEvent.Channel, domainEvent.Session.UserId);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task HandleAsync(ChannelParted domainEvent, CancellationToken cancellationToken = default)
	{
		domainEvent.Session.Notify(new Notification.ChannelParted(domainEvent.Channel.Name));
		NotifyOtherMembers(domainEvent.Channel, domainEvent.Session.UserId);
		return Task.CompletedTask;
	}

	private void NotifyOtherMembers(ChannelSession channel, int? skipUserId)
	{
		var notification = new Notification.ChannelInfoChanged(channel.Name);
		foreach (var memberId in channel.MemberIds)
		{
			if (memberId == skipUserId) continue;
			if (players.AllById.TryGetValue(memberId, out var session))
				session.Notify(notification);
		}
	}
}