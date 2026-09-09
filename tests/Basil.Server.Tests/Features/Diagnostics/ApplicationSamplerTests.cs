using Basil.Server.Features.Diagnostics;
using Basil.Server.Shared.Sessions;
using NSubstitute;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>Verifies <see cref="ApplicationSampler" /> reads Basil's own state from the registries and counters that already own it.</summary>
public class ApplicationSamplerTests
{
	[Fact]
	public void SampleReportsTheGameSessionRegistrysCurrentCount()
	{
		var gameSessions = Substitute.For<ISessionRegistry<GameSession>>();
		gameSessions.All.Returns(new List<GameSession>().AsReadOnly());
		var sampler = new ApplicationSampler(gameSessions, new RuntimeMeterListener());

		var sample = sampler.Sample();

		Assert.Equal(0, sample.ActiveGameSessions);
	}

	/// <summary>
	///     The eventing counters come from the same <see cref="RuntimeMeterListener" /> that owns
	///     every other push-based counter, folding every stream's tag value into one process-wide
	///     total rather than a per-stream breakdown.
	/// </summary>
	[Fact]
	public void SampleReportsTheMeterListenersEventingCounters()
	{
		var gameSessions = Substitute.For<ISessionRegistry<GameSession>>();
		gameSessions.All.Returns(new List<GameSession>().AsReadOnly());
		var listener = new RuntimeMeterListener();
		listener.RecordIntForTest("basil.sse.subscribers", 3, [new KeyValuePair<string, object?>("stream", "main")]);
		listener.RecordIntForTest("basil.sse.subscribers", 2, [new KeyValuePair<string, object?>("stream", "chat")]);
		listener.RecordForTest("basil.match.publish.stale_dropped", 1L,
			[new KeyValuePair<string, object?>("stream", "main")]);
		var sampler = new ApplicationSampler(gameSessions, listener);

		var sample = sampler.Sample();

		Assert.Equal(5, sample.ActiveSseSubscribers);
		Assert.Equal(1, sample.SsePublishesDropped);
	}
}
