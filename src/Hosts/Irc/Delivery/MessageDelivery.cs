using Basil.Application.Chat.Runtime;
using Basil.Application.Users.Connections;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc;
using Mediator;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Delivery;

/// <summary>Delivers a posted message to the IRC clients that may receive it, one line per line of text.</summary>
internal sealed class MessageDelivery(IrcClients clients, IOptions<IrcOptions> options)
	: INotificationHandler<ChannelMessagePosted>
{
	public async ValueTask Handle(ChannelMessagePosted notification, CancellationToken cancellationToken)
	{
		var (channel, message) = (notification.Channel, notification.Message);
		var author = message.Author;
		var prefix = IrcView.Mask(author, options.Value.Domain).ToString();
		var command = message.IsNotice ? "NOTICE" : "PRIVMSG";

		foreach (var connection in await channel.RecipientsAsync(notification, cancellationToken))
		{
			if (clients.Find(connection) is not { } session) continue;
			if (ReferenceEquals(connection, notification.By) && !session.Has("echo-message")) continue;

			var target = channel is PmChannel pm ? IrcView.Nick(pm.Owner.Record) : channel.Name;
			Send(session, command, prefix, target, message.Content, message.Timestamp, author);
		}

		// The sender of a private message is not a member of the recipient's channel, so the echo is separate.
		if (channel is PmChannel owner && notification.By is { } by && !ReferenceEquals(by.User, owner.Owner) &&
		    clients.Find(by) is { } own && own.Has("echo-message"))
			Send(own, command, prefix, IrcView.Nick(owner.Owner.Record), message.Content, message.Timestamp, author);

		if (notification is { AwayReply: { } away, By: { } awayTo } && clients.Find(awayTo) is { } awaySession)
			foreach (var line in IrcText.Lines(away.Content))
				awaySession.Deliver(client => client.Numeric(IrcNumericReply.RplAway, IrcView.Nick(away.Author), line));
	}

	private static void Send(IrcSession session, string command, string prefix, string target, string text,
		DateTimeOffset at, Basil.Domain.Users.UserRecord author)
	{
		foreach (var line in IrcText.Lines(text))
			session.Relay(new IrcMessage(command, prefix, target, line) { ForceTrailing = true }, author, at);
	}
}
