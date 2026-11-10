using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Stores an aborted round.</summary>
internal sealed class RoomRoundAbortedRecorder(IRoundRepository rounds) : INotificationHandler<RoomRoundAborted>
{
	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundAborted @event, CancellationToken ct)
	{
		await rounds.CreateOrUpdateAsync(@event.Round);
	}
}