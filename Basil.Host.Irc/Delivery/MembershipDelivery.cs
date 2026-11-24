using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Host.Irc.Clients;
using Basil.Host.Irc.Support;
using Basil.Host.Irc.Transport;
using Basil.Protocol.Irc.Models;
using Mediator;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Delivery;

internal sealed class ChannelJoinedDelivery(IrcClients clients, IrcPresence presence, IOptions<IrcOptions> options)
	: INotificationHandler<ChannelMemberJoined>
{
	public ValueTask Handle(ChannelMemberJoined notification, CancellationToken ct)
	{
		var channel = notification.Channel;
		if (channel is PmChannel or SpectatorChannel) return ValueTask.CompletedTask;

		var member = notification.Member;
		var firstOfUser = presence.Add(member.Record, channel);
		clients.DeliverTo(member, own => IrcChannels.SendJoin(own, channel, member.Record));
		if (!firstOfUser) return ValueTask.CompletedTask;

		var mask = IrcView.Mask(member.Record, options.Value.Domain);
		foreach (var session in clients.Of(channel.Members.Where(other => !ReferenceEquals(other.User, member.User))))
			session.Relay(session.Has("extended-join")
				? IrcMessage.Join(mask, channel.Name, member.Record.Value.Name, IrcView.Nick(member.Record))
				: IrcMessage.Join(mask, channel.Name), null, session.Now);
		return ValueTask.CompletedTask;
	}
}

internal sealed class ChannelPartedDelivery(IrcClients clients, IrcPresence presence, IOptions<IrcOptions> options)
	: INotificationHandler<ChannelMemberParted>
{
	public ValueTask Handle(ChannelMemberParted notification, CancellationToken ct)
	{
		var channel = notification.Channel;
		if (channel is PmChannel or SpectatorChannel) return ValueTask.CompletedTask;

		var (member, domain) = (notification.Member, options.Value.Domain);
		var mask = IrcView.Mask(member.Record, domain);
		if (clients.Find(member) is { } own)
			own.Deliver(notification.Kicked
				? Kick(own, channel, IrcReplies.KickRemoved)
				: IrcMessage.Part(mask, channel.Name));

		// A user who still has a connection in the channel, or who went offline (one QUIT says that), shows no PART.
		if (channel.Members.Any(other => ReferenceEquals(other.User, member.User)) || !member.Siblings.IsOnline ||
		    !presence.Remove(member.Record, channel))
			return ValueTask.CompletedTask;

		foreach (var session in clients.Of(channel.Members))
			session.Relay(IrcMessage.Part(mask, channel.Name), null, session.Now);
		return ValueTask.CompletedTask;
	}

	internal static IrcMessage Kick(IrcSocket session, Channel channel, string reason)
	{
		return IrcMessage.Kick(session.ServerName, channel.Name, session.Nick, reason);
	}
}

internal sealed class ChannelClosedDelivery(IrcClients clients, IrcPresence presence)
	: INotificationHandler<ChannelClosed>
{
	public ValueTask Handle(ChannelClosed notification, CancellationToken ct)
	{
		presence.Remove(notification.Channel);
		foreach (var session in clients.Of(notification.Members))
			session.Deliver(ChannelPartedDelivery.Kick(session, notification.Channel, IrcReplies.KickClosed));
		return ValueTask.CompletedTask;
	}
}

internal sealed class ChannelConfiguredDelivery(IrcClients clients) : INotificationHandler<ChannelConfigured>
{
	public ValueTask Handle(ChannelConfigured notification, CancellationToken ct)
	{
		foreach (var session in clients.Of(notification.Removed))
			session.Deliver(ChannelPartedDelivery.Kick(session, notification.Channel,
				IrcReplies.KickNoAccess));
		return ValueTask.CompletedTask;
	}
}