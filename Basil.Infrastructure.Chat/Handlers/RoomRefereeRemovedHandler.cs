using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class RoomRefereeRemovedHandler(RoomChannels channels) : INotificationHandler<RoomRefereeRemoved>
{
	public ValueTask Handle(RoomRefereeRemoved @event, CancellationToken ct)
	{
		channels.Find(@event.Room)?.ReleaseReferee(@event.Referee);
		return ValueTask.CompletedTask;
	}
}