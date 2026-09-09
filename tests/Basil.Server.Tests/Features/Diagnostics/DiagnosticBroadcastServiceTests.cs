using Basil.Server.Features.Diagnostics;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Sessions;
using NSubstitute;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>
///     Verifies <see cref="DiagnosticBroadcastService" />'s per-tick broadcast rule: a category with
///     no subscriber is never sampled or published, and an existing subscriber receives a fresh
///     reading the moment a tick runs.
/// </summary>
public class DiagnosticBroadcastServiceTests
{
	private static DiagnosticBroadcastService CreateService(ILiveEventHub hub)
	{
		var listener = new RuntimeMeterListener();
		var gameSessions = Substitute.For<ISessionRegistry<GameSession>>();
		gameSessions.All.Returns(new List<GameSession>().AsReadOnly());
		var process = new ProcessSampler();
		var gc = new GcSampler();
		var http = new HttpSampler(listener);
		var exceptions = new ExceptionsSampler(listener);
		var application = new ApplicationSampler(gameSessions, listener);
		var overview = new DiagnosticOverviewSampler(process, gc, http, exceptions, application);
		return new DiagnosticBroadcastService(hub, process, gc, http, exceptions, application, overview);
	}

	/// <summary>
	///     A category nobody is watching is skipped entirely -- proven by opening a subscription only
	///     after the pass runs and finding the stream still carries no version, rather than by counting
	///     internal calls into the sampler.
	/// </summary>
	[Fact]
	public void RunOnce_CategoryWithNoSubscriber_PublishesNothing()
	{
		var hub = new LiveEventHub();
		var service = CreateService(hub);

		service.RunOnce();
		using var subscription = hub.Open(DiagnosticStreams.Process);

		Assert.Equal(-1, subscription.Version);
		Assert.Null(subscription.Snapshot);
	}

	/// <summary>
	///     An existing subscriber's stream carries a fresh reading as soon as a pass runs. Read via
	///     <see cref="LiveSubscription.Events" />, not <see cref="LiveSubscription.Snapshot" />: a
	///     subscription that opened before anything had ever been published to its stream already
	///     counts as having "a snapshot" (an empty one) the moment it opens, so the reading this pass
	///     produces arrives as a queued event rather than replacing that snapshot in place.
	/// </summary>
	[Fact]
	public async Task RunOnce_CategoryWithASubscriber_DeliversAFreshReading()
	{
		var hub = new LiveEventHub();
		var service = CreateService(hub);
		using var subscription = hub.Open(DiagnosticStreams.Gc);

		service.RunOnce();

		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await using var events = subscription.Events.GetAsyncEnumerator(cancellation.Token);
		Assert.True(await events.MoveNextAsync());
		Assert.NotEmpty(events.Current.Payload.ToArray());
	}
}
