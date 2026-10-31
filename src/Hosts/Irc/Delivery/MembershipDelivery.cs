using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc;
using Mediator;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Delivery;

/// <summary>Tells IRC clients that a user entered a channel: the joining client gets the full welcome to the channel.</summary>
internal sealed class ChannelJoinedDelivery(IrcClients clients, IrcPresence presence, IOptions<IrcOptions> options)
	: INotificationHandler<ChannelMemberJoined>
{
	public ValueTask Handle(ChannelMemberJoined notification, CancellationToken cancellationToken)
	{
		var channel = notification.Channel;
		if (channel is PmChannel or SpectatorChannel) return ValueTask.CompletedTask;

		var member = notification.Member;
		var firstOfUser = presence.Add(member.Record, channel);
		if (clients.Find(member) is { } own) IrcChannels.SendJoin(own, channel, member.Record);
		if (!firstOfUser) return ValueTask.CompletedTask;

		var mask = IrcView.Mask(member.Record, options.Value.Domain).ToString();
		foreach (var session in clients.Of(channel.Members.Where(other => !ReferenceEquals(other.User, member.User))))
			session.Relay(session.Has("extended-join")
				? new IrcMessage("JOIN", mask, channel.Name, member.Record.Value.Name, IrcView.Nick(member.Record))
				: new IrcMessage("JOIN", mask, channel.Name), null, session.Now);
		return ValueTask.CompletedTask;
	}
}

/// <summary>Tells IRC clients that a user left a channel, or that the server removed the client from it.</summary>
internal sealed class ChannelPartedDelivery(IrcClients clients, IrcPresence presence, IOptions<IrcOptions> options)
	: INotificationHandler<ChannelMemberParted>
{
	public ValueTask Handle(ChannelMemberParted notification, CancellationToken cancellationToken)
	{
		var channel = notification.Channel;
		if (channel is PmChannel or SpectatorChannel) return ValueTask.CompletedTask;

		var (member, domain) = (notification.Member, options.Value.Domain);
		var mask = IrcView.Mask(member.Record, domain).ToString();
		if (clients.Find(member) is { } own)
			own.Deliver(notification.Kicked
				? Kick(own, channel, "Removed from the channel")
				: new IrcMessage("PART", mask, channel.Name));

		// A user who still has a connection in the channel, or who went offline (one QUIT says that), shows no PART.
		if (channel.Members.Any(other => ReferenceEquals(other.User, member.User)) || !member.User.IsOnline ||
		    !presence.Remove(member.Record, channel))
			return ValueTask.CompletedTask;

		foreach (var session in clients.Of(channel.Members))
			session.Relay(new IrcMessage("PART", mask, channel.Name), null, session.Now);
		return ValueTask.CompletedTask;
	}

	internal static IrcMessage Kick(IrcSession session, Channel channel, string reason)
	{
		return new IrcMessage("KICK", session.ServerName, channel.Name, session.Nick, reason);
	}
}

/// <summary>Tells the IRC clients of a channel that it closed.</summary>
internal sealed class ChannelClosedDelivery(IrcClients clients, IrcPresence presence)
	: INotificationHandler<ChannelClosed>
{
	public ValueTask Handle(ChannelClosed notification, CancellationToken cancellationToken)
	{
		presence.Remove(notification.Channel);
		foreach (var session in clients.Of(notification.Members))
			session.Deliver(ChannelPartedDelivery.Kick(session, notification.Channel, "The channel was closed"));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Tells the IRC clients that a channel's new read rules removed them from it.</summary>
internal sealed class ChannelConfiguredDelivery(IrcClients clients) : INotificationHandler<ChannelConfigured>
{
	public ValueTask Handle(ChannelConfigured notification, CancellationToken cancellationToken)
	{
		foreach (var session in clients.Of(notification.Removed))
			session.Deliver(ChannelPartedDelivery.Kick(session, notification.Channel, "You may no longer read the channel"));
		return ValueTask.CompletedTask;
	}
}
