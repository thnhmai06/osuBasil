using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

/// <summary>Brings a newly opened connection into its user's private-message channel.</summary>
internal sealed class PmConnectionOpenedHandler(PmChannels channels) : INotificationHandler<UserConnectionOpened>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionOpened @event, CancellationToken ct)
	{
		if (@event.Connection.Type is not SessionType.StableTourney)
			channels.Of(@event.Connection.User).ScopeTo(@event.Connection).Join();

		return ValueTask.CompletedTask;
	}
}