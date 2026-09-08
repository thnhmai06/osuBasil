using System.Text;
using Basil.Server.Shared.Eventing;

namespace Basil.Application.Tests.Shared.Eventing;

public class LiveEventHubTests
{
	// ReadOnlyMemory<byte>.Equals compares the underlying array reference, not its content
	// (measured directly: two separately-allocated arrays with identical bytes are not "Equal").
	// Interning by content here lets Assert.Equal on a ReadOnlyMemory<byte>? still mean something:
	// two calls with the same literal always resolve to the same array, while two different
	// literals never accidentally collide.
	private static readonly Dictionary<string, byte[]> BytesCache = new();

	private static ReadOnlyMemory<byte> Bytes(string s)
	{
		if (!BytesCache.TryGetValue(s, out var bytes))
			BytesCache[s] = bytes = Encoding.UTF8.GetBytes(s);
		return bytes;
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
	///     A subscriber never receives an event the snapshot it opened with already contains, and
	///     never receives one out of order. This is the SSE contract, not an implementation detail:
	///     clients apply an item only when its version exceeds the last one they applied.
	/// </summary>
	[Fact]
	public async Task SubscriberNeverReceivesAnEventAtOrBelowItsSnapshotVersion()
	{
		var hub = new LiveEventHub();
		var key = new StreamKey("match", 1, "main");

		hub.Publish(key, 42, Bytes("v42"));

		await using var sub = hub.Open(key);
		hub.Publish(key, 43, Bytes("v43"));
		hub.Publish(key, 44, Bytes("v44"));

		Assert.Equal(42, sub.Version);
		var received = await TakeAsync(sub.Events, 2);
		Assert.Equal([43L, 44L], received.Select(e => e.Version));
	}

	[Fact]
	public void PublishWithoutSubscribersIsSkippedByTheCallerAndMarksTheStreamStale()
	{
		var hub = new LiveEventHub();
		var key = new StreamKey("match", 1, "main");

		Assert.False(hub.HasSubscribers(key));
		hub.MarkStale(key, 7);

		using var sub = hub.Open(key);
		Assert.True(sub.IsStale);
	}

	/// <summary>
	///     A snapshot built while unsubscribed loses to a publish that landed in the meantime, so a
	///     freshly built but older snapshot can never roll a client back.
	/// </summary>
	[Fact]
	public void SeedLosesToAPublishThatLandedWhileTheSnapshotWasBeingBuilt()
	{
		var hub = new LiveEventHub();
		var key = new StreamKey("match", 1, "main");
		hub.MarkStale(key, 10);

		using var sub = hub.Open(key);
		var fence = sub.Version;

		hub.Publish(key, 11, Bytes("published-11"));
		var seeded = sub.SeedIfNotSuperseded(Bytes("built-from-10"), fence);

		Assert.False(seeded);
		Assert.Equal(Bytes("published-11"), sub.Snapshot);
	}
}