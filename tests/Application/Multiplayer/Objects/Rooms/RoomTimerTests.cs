using System.Diagnostics.Metrics;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Application.Multiplayer.Tests.Objects.Rooms;

/// <summary>
///     A room timer whose work fails neither ends the process nor goes unnoticed: the failure is counted on the
///     server's meter, and the timer returns normally.
/// </summary>
public sealed class RoomTimerTests
{
	[Fact]
	public void WorkThatThrows_IsCountedAndDoesNotThrow()
	{
		var seen = new List<long>();
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, meterListener) =>
		{
			if (instrument.Name == RoomMetrics.TimerFailedName) meterListener.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
		{
			lock (seen)
			{
				seen.Add(value);
			}
		});
		listener.Start();

		var exception = Record.Exception(() => Room.Fire(() => throw new InvalidOperationException()));

		Assert.Null(exception);
		lock (seen)
		{
			Assert.Equal(1, seen.Sum());
		}
	}
}