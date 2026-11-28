using Basil.Application.Chat.Objects.Events;
using Basil.Application.Shared.Replies;
using Basil.Application.Sessions.Objects.Events;
using Basil.Application.Users.Objects;
using Basil.Host.Irc.Clients;
using Basil.Host.Irc.Support;
using Basil.Host.Irc.Transport;
using Basil.Protocol.Irc.Models;
using Mediator;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Delivery;

internal sealed class ConnectionClosedDelivery(IrcClients clients, IReplies<IrcReply> replies)
	: INotificationHandler<SessionClosed>
{
	public ValueTask Handle(SessionClosed notification, CancellationToken ct)
	{
		var connection = notification.Session;
		if (clients.Find(connection) is { } own) own.Close(replies.Text(IrcReplies.Closing(notification.Reason)));
		clients.Forget(connection);
		return ValueTask.CompletedTask;
	}
}

internal sealed class ChatQuitDelivery(IrcClients clients, IReplies<IrcReply> replies, IOptions<IrcOptions> options)
	: INotificationHandler<ChatQuit>
{
	public ValueTask Handle(ChatQuit notification, CancellationToken ct)
	{
		var quit = IrcMessage.Quit(IrcView.Mask(notification.Session.Record, options.Value.Domain),
			replies.Text(IrcReplies.Closing(notification.Reason)));
		foreach (var session in clients.Of(notification.Audience))
			session.Relay(quit, null, session.Now);
		return ValueTask.CompletedTask;
	}
}