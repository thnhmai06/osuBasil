using Basil.Application.Users.Users;
using Mediator;

namespace Basil.Application.Chat.Runtime;

/// <summary>Takes a closed connection out of its user's private-message channel and closes the channel when the user goes offline.</summary>
internal sealed class PmConnectionClosedHandler(PmChannels channels) : INotificationHandler<UserConnectionClosed>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionClosed @event, CancellationToken cancellationToken)
	{
		var user = @event.Connection.User;
		if (channels.Find(user) is { } channel)
			channel.ScopeTo(@event.Connection).Part();

		if (@event.WentOffline && channels.Remove(user) is { } closed)
			closed.Close();

		return ValueTask.CompletedTask;
	}
}