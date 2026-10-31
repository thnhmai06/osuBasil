using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Mediator;
using Basil.Application.Chat.Registry;

namespace Basil.Application.Chat.Handlers;

/// <summary>Brings a newly opened connection into its user's private-message channel.</summary>
internal sealed class PmConnectionOpenedHandler(PmChannels channels) : INotificationHandler<UserConnectionOpened>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionOpened @event, CancellationToken cancellationToken)
	{
		if (@event.Connection.Type is not SessionType.OsuTourney)
			channels.Of(@event.Connection.User).ScopeTo(@event.Connection).Join();

		return ValueTask.CompletedTask;
	}
}