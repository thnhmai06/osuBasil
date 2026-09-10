using Basil.Server.Features.Diagnostics;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>
///     Verifies <see cref="HttpSampler" /> reports the web host's counters, and that its two ways of
///     reading the request-duration distribution keep their contract: <see cref="HttpSampler.Sample" />
///     never resets it, and <see cref="HttpSampler.SampleAndResetDuration" /> always does.
/// </summary>
public class HttpSamplerTests
{
	[Fact]
	public void SampleReportsTheMeterListenersRequestAndConnectionCounters()
	{
		var listener = new RuntimeMeterListener();
		listener.RecordForTest("http.server.active_requests", 2, []);
		listener.RecordForTest("kestrel.active_connections", 4, []);
		listener.RecordForTest("http.server.request.duration", 0.02, []);
		listener.RecordForTest("kestrel.connection.duration", 0.01, []);
		var sampler = new HttpSampler(listener);

		var sample = sampler.Sample();

		Assert.Equal(2, sample.ActiveRequests);
		Assert.Equal(1, sample.RequestsCompleted);
		Assert.Equal(0, sample.RequestsFailed);
		Assert.Equal(4, sample.ActiveConnections);
		Assert.Equal(1, sample.ConnectionsCompleted);
	}

	[Fact]
	public void SampleDoesNotResetTheRequestDurationWindow()
	{
		var listener = new RuntimeMeterListener();
		listener.RecordForTest("http.server.request.duration", 0.02, []);
		var sampler = new HttpSampler(listener);

		var first = sampler.Sample();
		var second = sampler.Sample();

		Assert.Equal(1, first.RequestDuration.Count);
		Assert.Equal(1, second.RequestDuration.Count);
	}

	[Fact]
	public void SampleAndResetDurationEmptiesTheWindowForTheNextInterval()
	{
		var listener = new RuntimeMeterListener();
		listener.RecordForTest("http.server.request.duration", 0.02, []);
		var sampler = new HttpSampler(listener);

		var first = sampler.SampleAndResetDuration();
		var second = sampler.SampleAndResetDuration();

		Assert.Equal(1, first.RequestDuration.Count);
		Assert.Equal(0, second.RequestDuration.Count);
	}
}
