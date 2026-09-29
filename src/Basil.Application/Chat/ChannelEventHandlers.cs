using Notification = Basil.Application.Chat;
using Basil.Application.Common.Events;
using Basil.Application.Common.Notifications;
using Basil.Application.Sessions;
namespace Basil.Application.Chat;

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
