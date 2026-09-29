using Basil.Application.Contracts.Events;
using Basil.Application.Models.Sessions;
using Notification = Basil.Application.Models.Notifications;

namespace Basil.Application.Services.EventHandlers;

/// <summary>Reacts to a channel's membership events: a session joining or parting.</summary>
public sealed class ChannelEventHandlers :
	IEventHandler<ChannelMembershipEvent>
{
	/// <inheritdoc />
	public Task HandleAsync(ChannelMembershipEvent domainEvent, CancellationToken cancellationToken = default)
	{
		switch (domainEvent)
		{
			case MemberJoined joined:
				joined.Member.Notify(new Notification.ChannelJoined(joined.Channel));
				NotifyOtherMembers(joined.Channel, joined.Member);
				break;
			case MemberParted parted:
				parted.Member.Notify(new Notification.ChannelParted(parted.Channel));
				NotifyOtherMembers(parted.Channel, parted.Member);
				break;
		}

		return Task.CompletedTask;
	}

	private void NotifyOtherMembers(ChannelSession channel, UserSession skip)
	{
		var notification = new Notification.ChannelInfoChanged(channel);
		foreach (var member in channel.Members)
		{
			if (ReferenceEquals(member, skip)) continue;
			member.Notify(notification);
		}
	}
}
