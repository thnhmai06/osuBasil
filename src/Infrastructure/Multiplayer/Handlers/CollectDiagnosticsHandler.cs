using Basil.Application.Shared.Diagnostics;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Reports the open rooms and the rounds in progress.</summary>
internal sealed class CollectDiagnosticsHandler(Lobby lobby) : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "rooms", lobby.Rooms.Count));
		notification.Collector.Add(new DiagnosticsValue("application", "roundsInProgress",
			lobby.Rooms.Values.Count(room => room.Rounds.InProgress)));
		return ValueTask.CompletedTask;
	}
}