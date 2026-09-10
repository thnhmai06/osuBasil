using System.Diagnostics.Metrics;
using Basil.Server.Features.Irc;
using Basil.Server.Shared.Sessions;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Server.Tests.Features.Irc;

/// <summary>
///     Verifies the observable gauge <see cref="IrcMetricsPublisher" /> publishes reports the
///     registry's real session count, read back the same way Diagnostics will: through a
///     <see cref="MeterListener" /> attached to the shared meter, never through a direct reference to
///     the registry.
/// </summary>
public class IrcMetricsPublisherTests
{
	private static IrcSession MakeIrc(int id)
	{
		return new IrcSession(id, $"user{id}", $"token{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = Substitute.For<IIrcConnection>()
		};
	}

	[Fact]
	public async Task Gauge_ReportsRegistryCount()
	{
		var registry = Substitute.For<ISessionRegistry<IrcSession>>();
		registry.All.Returns(new[] { MakeIrc(1), MakeIrc(2), MakeIrc(3) });

		var publisher = new IrcMetricsPublisher(registry);
		await publisher.StartAsync(CancellationToken.None);
		try
		{
			var values = new Dictionary<string, int>();
			using var listener = new MeterListener
			{
				InstrumentPublished = (instrument, l) =>
				{
					if (instrument.Meter.Name == "Basil" && instrument.Name == "basil.irc.sessions.active")
						l.EnableMeasurementEvents(instrument);
				}
			};
			listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
				values[instrument.Name] = measurement);
			listener.Start();

			listener.RecordObservableInstruments();

			Assert.Equal(3, values["basil.irc.sessions.active"]);
		}
		finally
		{
			await publisher.StopAsync(CancellationToken.None);
		}
	}
}
