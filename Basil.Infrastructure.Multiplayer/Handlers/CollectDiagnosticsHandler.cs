using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class CollectDiagnosticsHandler(Lobby lobby) : INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "rooms", lobby.Rooms.Count));
		notification.Collector.Add(new DiagnosticsValue("application", "roundsInProgress",
			lobby.Rooms.Values.Count(room => room.Rounds.InProgress)));
		return ValueTask.CompletedTask;
	}
}