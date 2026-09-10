using System.Diagnostics.Metrics;
using Basil.Server.Features.Chat;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Server.Tests.Features.Chat;

/// <summary>
///     Verifies the observable gauge <see cref="ChatMetricsPublisher" /> publishes reports the
///     registry's real channel count, read back the same way Diagnostics will: through a
///     <see cref="MeterListener" /> attached to the shared meter, never through a direct reference to
///     the registry.
/// </summary>
public class ChatMetricsPublisherTests
{
	[Fact]
	public async Task Gauge_ReportsRegistryCount()
	{
		var registry = Substitute.For<IChannelRegistry>();
		registry.All.Returns(
		[
			new ChannelSession(1, "#osu", 0, (UserPrivileges)0, true),
			new ChannelSession(2, "#staff", UserPrivileges.Staff, UserPrivileges.Staff, true)
		]);

		var publisher = new ChatMetricsPublisher(registry);
		await publisher.StartAsync(CancellationToken.None);
		try
		{
			var values = new Dictionary<string, int>();
			using var listener = new MeterListener
			{
				InstrumentPublished = (instrument, l) =>
				{
					if (instrument.Meter.Name == "Basil" && instrument.Name == "basil.channels.active")
						l.EnableMeasurementEvents(instrument);
				}
			};
			listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
				values[instrument.Name] = measurement);
			listener.Start();

			listener.RecordObservableInstruments();

			Assert.Equal(2, values["basil.channels.active"]);
		}
		finally
		{
			await publisher.StopAsync(CancellationToken.None);
		}
	}
}
