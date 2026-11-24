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

internal sealed class ConnectionClosedDelivery(IrcClients clients,
	IrcPresence presence,
	IReplies<IrcReply> replies,
	IOptions<IrcOptions> options)
	: INotificationHandler<SessionClosed>
{
	public ValueTask Handle(SessionClosed notification, CancellationToken ct)
	{
		var connection = notification.Session;
		if (clients.Find(connection) is { } own) own.Close(replies.Text(IrcReplies.Closing(notification.Reason)));
		clients.Forget(connection);
		if (!notification.WentOffline) return ValueTask.CompletedTask;

		var channels = presence.Take(connection.Record);
		var quit = IrcMessage.Quit(IrcView.Mask(connection.Record, options.Value.Domain),
			replies.Text(IrcReplies.Closing(notification.Reason)));
		foreach (var session in clients.All.Where(session => session.Session is { } other &&
		                                                     !ReferenceEquals(other.User, connection.User) &&
		                                                     channels.Any(channel => channel.Members.Contains(other))))
			session.Relay(quit, null, session.Now);
		return ValueTask.CompletedTask;
	}
}