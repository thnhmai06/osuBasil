using System.Diagnostics.Metrics;
using Basil.Application.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Infrastructure.Multiplayer;
using NSubstitute;

namespace Basil.Infrastructure.Tests.Multiplayer;

/// <summary>
///     Verifies the observable gauges <see cref="MultiplayerMetricsPublisher" /> publishes report the
///     registry's real counts, read back the same way Diagnostics will: through a
///     <see cref="MeterListener" /> attached to the shared meter, never through a direct reference to
///     the registry.
/// </summary>
public class MultiplayerMetricsPublisherTests
{
	private static MatchSession MakeMatch(int id)
	{
		return new MatchSession(
			id, "test match", "pw",
			null, null,
			GameMode.Standard, Mods.NoMod, MatchWinCondition.Score,
			MatchTeamType.HeadToHead, false, 0);
	}

	[Fact]
	public async Task Gauges_ReportRegistryCountAndPendingTimerCount()
	{
		var withTimer = MakeMatch(1);
		withTimer.PendingTimer = new CancellationTokenSource();
		var withoutTimer = MakeMatch(2);

		var registry = Substitute.For<IMatchRegistry>();
		registry.All.Returns(new[] { withTimer, withoutTimer });

		var publisher = new MultiplayerMetricsPublisher(registry);
		await publisher.StartAsync(CancellationToken.None);
		try
		{
			var values = new Dictionary<string, int>();
			using var listener = new MeterListener();
			listener.InstrumentPublished = (instrument, l) =>
			{
				if (instrument.Meter.Name == "Basil" &&
				    instrument.Name is "basil.matches.active" or "basil.match.timers.active")
					l.EnableMeasurementEvents(instrument);
			};
			listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
				values[instrument.Name] = measurement);
			listener.Start();

			listener.RecordObservableInstruments();

			Assert.Equal(2, values["basil.matches.active"]);
			Assert.Equal(1, values["basil.match.timers.active"]);
		}
		finally
		{
			await publisher.StopAsync(CancellationToken.None);
		}
	}
}