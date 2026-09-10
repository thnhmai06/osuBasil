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
	///     A category nobody is watching doesn't crash the tick loop. The hub carries no retained
	///     state to inspect after the fact (it forwards only to subscribers present at publish time),
	///     so unlike a subscriber-present tick this can't assert delivery -- only that skipping a
	///     category is not distinguishable from a failure.
	/// </summary>
	[Fact]
	public void RunOnce_CategoryWithNoSubscriber_DoesNotThrow()
	{
		var hub = new LiveEventHub();
		var service = CreateService(hub);

		var exception = Record.Exception(service.RunOnce);

		Assert.Null(exception);
	}

	/// <summary>
	///     An existing subscriber's stream carries a fresh reading as soon as a pass runs, delivered on
	///     <see cref="LiveSubscription.Events" /> -- the hub carries no snapshot of its own.
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

	/// <summary>
	///     Pins the design this whole broadcast loop exists for (see <see cref="LiveEventHub" />'s own
	///     "every subscriber to a stream receives the same immutable event" remark, and Task 5.7's
	///     overhead measurement): a tick samples one category once and hands the identical payload
	///     buffer to every subscriber, rather than sampling once per connection.
	///
	///     The tick's publish arrives on <see cref="LiveSubscription.Events" /> for all three.
	///
	///     Compared via <see cref="ReadOnlyMemory{T}.Equals(ReadOnlyMemory{T})" />, which compares the
	///     underlying buffer reference (plus offset and length), not byte content -- so this fails if
	///     the implementation ever starts serializing a fresh payload per subscriber, even when two
	///     independently-sampled readings happen to carry the same values.
	/// </summary>
	[Fact]
	public async Task RunOnce_MultipleSubscribersToOneCategory_AllReceiveTheSamePublishedBuffer()
	{
		var hub = new LiveEventHub();
		var service = CreateService(hub);
		using var first = hub.Open(DiagnosticStreams.Overview);
		using var second = hub.Open(DiagnosticStreams.Overview);
		using var third = hub.Open(DiagnosticStreams.Overview);

		service.RunOnce();

		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var firstEvent = await FirstEventAsync(first, cancellation.Token);
		var secondEvent = await FirstEventAsync(second, cancellation.Token);
		var thirdEvent = await FirstEventAsync(third, cancellation.Token);

		Assert.Equal(firstEvent.Version, secondEvent.Version);
		Assert.Equal(firstEvent.Version, thirdEvent.Version);
		Assert.True(firstEvent.Payload.Equals(secondEvent.Payload));
		Assert.True(firstEvent.Payload.Equals(thirdEvent.Payload));
	}

	private static async Task<LiveEvent> FirstEventAsync(LiveSubscription subscription,
		CancellationToken cancellationToken)
	{
		await using var events = subscription.Events.GetAsyncEnumerator(cancellationToken);
		Assert.True(await events.MoveNextAsync());
		return events.Current;
	}
}