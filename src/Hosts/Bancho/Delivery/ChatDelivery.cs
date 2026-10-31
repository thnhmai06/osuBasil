using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Domain.Auth;
using Basil.Host.Bancho.Support;
using Mediator;

namespace Basil.Host.Bancho.Delivery;

/// <summary>Tells a client that it joined a channel.</summary>
internal sealed class ChannelJoinedDelivery(Outbox outbox) : INotificationHandler<ChannelMemberJoined>
{
	public ValueTask Handle(ChannelMemberJoined notification, CancellationToken cancellationToken)
	{
		var channel = notification.Channel;
		if (channel is PmChannel) return ValueTask.CompletedTask;
		var name = BanchoView.ChannelName(channel);
		outbox.Send(notification.Member, writer => writer.ChannelJoin(name));
		outbox.Send(channel.Members, writer => writer.ChannelInfo(name, channel.Topic, channel.Members.Count));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Tells a client that the server removed it from a channel.</summary>
internal sealed class ChannelPartedDelivery(Outbox outbox) : INotificationHandler<ChannelMemberParted>
{
	public ValueTask Handle(ChannelMemberParted notification, CancellationToken cancellationToken)
	{
		var channel = notification.Channel;
		if (channel is PmChannel) return ValueTask.CompletedTask;
		var name = BanchoView.ChannelName(channel);
		if (notification.Kicked) outbox.Send(notification.Member, writer => writer.ChannelKick(name));
		outbox.Send(channel.Members, writer => writer.ChannelInfo(name, channel.Topic, channel.Members.Count));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Tells the members of a channel that it closed.</summary>
internal sealed class ChannelClosedDelivery(Outbox outbox) : INotificationHandler<ChannelClosed>
{
	public ValueTask Handle(ChannelClosed notification, CancellationToken cancellationToken)
	{
		if (notification.Channel is PmChannel) return ValueTask.CompletedTask;
		var name = BanchoView.ChannelName(notification.Channel);
		outbox.Send(notification.Members, writer => writer.ChannelKick(name));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Delivers a posted message to the osu! clients that may receive it.</summary>
internal sealed class MessageDelivery(Outbox outbox) : INotificationHandler<ChannelMessagePosted>
{
	public async ValueTask Handle(ChannelMessagePosted notification, CancellationToken cancellationToken)
	{
		var (channel, message) = (notification.Channel, notification.Message);
		var author = message.Author;
		var target = channel is PmChannel pm ? pm.Owner.Record.Value.Name : BanchoView.ChannelName(channel);
		var recipients = (await channel.RecipientsAsync(notification, cancellationToken))
			.Where(recipient => recipient.Type is not SessionType.Bot &&
			                    !(recipient.Type is SessionType.Osu && recipient.Record.Equals(author)));
		outbox.Send(recipients, writer =>
			writer.SendMessage(author.Value.Name, message.Content, target, author.Id));
		if (notification is { AwayReply: { } away, By: { } by })
			outbox.Send(by, writer => writer.SendMessage(away.Author.Value.Name, away.Content,
				author.Value.Name, away.Author.Id));
	}
}
