using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Mediator;

namespace Basil.Application.Chat.Runtime;

/// <summary>Brings a newly opened connection into its user's private-message channel.</summary>
internal sealed class PmConnectionOpenedHandler(PmChannels channels) : INotificationHandler<UserConnectionOpened>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionOpened @event, CancellationToken cancellationToken)
	{
		if (@event.Connection.Type is not ConnectionType.Tourney)
			channels.Of(@event.Connection.User).ScopeTo(@event.Connection).Join();

		return ValueTask.CompletedTask;
	}
}