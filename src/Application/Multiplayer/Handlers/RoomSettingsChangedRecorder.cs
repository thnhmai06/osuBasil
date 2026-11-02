using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Application.Multiplayer.Handlers;

/// <summary>Stores the match again when its name or privacy changed.</summary>
internal sealed class RoomSettingsChangedRecorder(IMatchRepository matches) : INotificationHandler<RoomSettingsChanged>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomSettingsChanged @event, CancellationToken cancellationToken)
	{
		if (@event.Change.Name is not null || @event.Change.IsPrivate is not null)
			await matches.UpdateAsync(@event.Room.Match, cancellationToken);
	}
}
