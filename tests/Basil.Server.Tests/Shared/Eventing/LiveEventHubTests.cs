using System.Text;
using Basil.Server.Shared.Eventing;

namespace Basil.Server.Tests.Shared.Eventing;

public class LiveEventHubTests
{
	private static ReadOnlyMemory<byte> Bytes(string s)
	{
		return Encoding.UTF8.GetBytes(s);
	}

	private static async Task<List<LiveEvent>> TakeAsync(IAsyncEnumerable<LiveEvent> source, int count)
	{
		var results = new List<LiveEvent>();
		await foreach (var item in source)
		{
			results.Add(item);
			if (results.Count == count) break;
		}

		return results;
	}

	/// <summary>
	///     A subscriber never receives an event at or below a version it already has, and never
	///     receives one out of order. This is the SSE contract, not an implementation detail: clients
	///     apply an item only when its version exceeds the last one they applied.
	/// </summary>
	[Fact]
	public async Task SubscriberNeverReceivesAnEventAtOrBelowItsFenceVersion()
	{
		var hub = new LiveEventHub();
		var key = new StreamKey("match", 1, "main");

		hub.Publish(key, 42, Bytes("v42"));

		await using var sub = hub.Open(key);
		hub.Publish(key, 43, Bytes("v43"));
		hub.Publish(key, 44, Bytes("v44"));

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var received = await TakeAsync(sub.EventsAfter(42, cts.Token), 2);
		Assert.Equal([43L, 44L], received.Select(e => e.Version));
	}

	private sealed record TestState(string Value);

	/// <summary>
	///     Pins the invariant the deleted seed handshake used to defend: a subscriber's first item is
	///     the state at the version it read from its own state store, and every later item the hub
	///     delivers has a version strictly greater than that. The hub itself carries no snapshot -- a
	///     caller establishes the fence from <see cref="StateStream{T}.GetLatestAndVersion" />, reads
	///     it as its first item, then drops anything from the hub at or below it.
	/// </summary>
	[Fact]
	public async Task SubscriberOpensAtStateVersionAndEveryLaterItemIsStrictlyNewer()
	{
		var hub = new LiveEventHub();
		var stream = new StateStream<TestState>("test");
		var key = new StreamKey("match", 1, "main");

		// A publish before anyone subscribes: reflected in the state store, never delivered as an event.
		var delta1 = stream.Publish(new TestState("a"), 1);
		Assert.NotNull(delta1);
		hub.Publish(key, 1, delta1);

		await using var subscription = hub.Open(key);
		var (latest, fence) = stream.GetLatestAndVersion();

		Assert.Equal(new TestState("a"), latest);
		Assert.Equal(1, fence);

		var delta2 = stream.Publish(new TestState("b"), 2);
		Assert.NotNull(delta2);
		hub.Publish(key, 2, delta2);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var received = await TakeAsync(subscription.EventsAfter(fence, cts.Token), 1);

		Assert.Equal([2L], received.Select(e => e.Version));
	}
}