using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc;
using Mediator;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Delivery;

/// <summary>Ends the socket of a connection the server closed and tells the other clients when a user goes offline.</summary>
internal sealed class ConnectionClosedDelivery(IrcClients clients, IrcPresence presence, IOptions<IrcOptions> options)
	: INotificationHandler<UserConnectionClosed>
{
	public ValueTask Handle(UserConnectionClosed notification, CancellationToken cancellationToken)
	{
		var connection = notification.Connection;
		if (clients.Find(connection) is { } own) own.Close(IrcReplies.Closing(notification.Reason));
		if (!notification.WentOffline) return ValueTask.CompletedTask;

		var channels = presence.Take(connection.Record);
		var quit = new IrcMessage("QUIT", IrcView.Mask(connection.Record, options.Value.Domain).ToString(),
			IrcReplies.Closing(notification.Reason));
		foreach (var session in clients.All.Where(session => session.Connection is { } other &&
		                                                      !ReferenceEquals(other.User, connection.User) &&
		                                                      channels.Any(channel => channel.Members.Contains(other))))
			session.Relay(quit, null, session.Now);
		return ValueTask.CompletedTask;
	}
}
