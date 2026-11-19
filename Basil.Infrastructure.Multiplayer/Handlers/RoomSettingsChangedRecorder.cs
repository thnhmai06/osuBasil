using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class RoomSettingsChangedRecorder(IMatchRepository matches) : INotificationHandler<RoomSettingsChanged>
{
	public async ValueTask Handle(RoomSettingsChanged @event, CancellationToken ct)
	{
		if (@event.Change.Name is not null || @event.Change.IsPrivate is not null)
			await matches.UpdateAsync(@event.Room.Match);
	}
}