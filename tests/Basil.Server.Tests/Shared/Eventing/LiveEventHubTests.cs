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
		// ReadOnlyMemory<byte>.Equals compares the underlying array reference, not content
		// (measured directly), so the assertion decodes both sides to compare what they say.
		Assert.Equal("published-11", Encoding.UTF8.GetString(sub.Snapshot!.Value.Span));
	}
}