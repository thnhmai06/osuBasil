using Basil.Server.Features.Diagnostics;
using Basil.Server.Shared.Sessions;
using NSubstitute;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>
///     Verifies <see cref="DiagnosticOverviewSampler" /> combines the curated fields from each of its
///     underlying samplers, and that it never resets the shared request-duration window (that
///     ownership belongs to the <c>http</c> category's own live stream).
/// </summary>
public class DiagnosticOverviewSamplerTests
{
	[Fact]
	public void SampleCombinesTheCuratedFieldsFromEachUnderlyingSampler()
	{
		var gameSessions = Substitute.For<ISessionRegistry<GameSession>>();
		gameSessions.All.Returns(new List<GameSession>().AsReadOnly());
		var listener = new RuntimeMeterListener();
		listener.RecordForTest("http.server.active_requests", 3, []);
		listener.RecordForTest("dotnet.exceptions", 1, []);
		listener.RecordIntForTest("basil.matches.active", 2, []);
		var sampler = new DiagnosticOverviewSampler(new ProcessSampler(), new GcSampler(), new HttpSampler(listener),
			new ExceptionsSampler(listener), new ApplicationSampler(gameSessions, listener));

		var sample = sampler.Sample();

		Assert.Equal(3, sample.ActiveHttpRequests);
		Assert.Equal(1, sample.ExceptionsThrown);
		Assert.Equal(0, sample.ActiveGameSessions);
		Assert.Equal(2, sample.ActiveMatches);
	}

	[Fact]
	public void SampleDoesNotResetTheHttpCategorysRequestDurationWindow()
	{
		var gameSessions = Substitute.For<ISessionRegistry<GameSession>>();
		gameSessions.All.Returns(new List<GameSession>().AsReadOnly());
		var listener = new RuntimeMeterListener();
		listener.RecordForTest("http.server.request.duration", 0.02, []);
		var httpSampler = new HttpSampler(listener);
		var sampler = new DiagnosticOverviewSampler(new ProcessSampler(), new GcSampler(), httpSampler,
			new ExceptionsSampler(listener), new ApplicationSampler(gameSessions, listener));

		sampler.Sample();
		var stillThere = httpSampler.Sample();

		Assert.Equal(1, stillThere.RequestDuration.Count);
	}
}
