using Basil.Application.Multiplayer.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Multiplayer.Workers;

/// <summary>Closes the rooms that stayed empty for too long, and announces their closing, every 30 seconds.</summary>
internal sealed class EmptyRoomCloser(
	Lobby lobby,
	TimeProvider time,
	ILogger<EmptyRoomCloser> logger) : BackgroundService
{
	/// <summary>The interval between two checks of the open rooms.</summary>
	internal static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Period, time);
		while (await timer.WaitForNextTickAsync(stoppingToken))
			try
			{
				var closed = lobby.CloseEmptyRooms();
				if (closed > 0) logger.LogDebug("Closed {RoomCount} empty rooms", closed);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Could not close empty rooms");
			}
	}
}
