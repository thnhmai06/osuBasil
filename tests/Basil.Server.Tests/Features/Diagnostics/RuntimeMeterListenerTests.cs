using Basil.Server.Features.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Basil.Server.Tests.Features.Diagnostics;

/// <summary>
///     Verifies <see cref="RuntimeMeterListener" />'s lifetime and memory-boundedness contract: one
///     instance for the whole process, and a fixed number of tracked series regardless of tag
///     cardinality.
/// </summary>
public class RuntimeMeterListenerTests
{
	/// <summary>Exactly one listener exists for the process, owned by the host, never by a subscriber.</summary>
	[Fact]
	public void TheListenerIsASingletonHostedService()
	{
		var services = new ServiceCollection();
		services.AddSingleton<RuntimeMeterListener>();
		services.AddHostedService(sp => sp.GetRequiredService<RuntimeMeterListener>());
		using var provider = services.BuildServiceProvider();

		var a = provider.GetRequiredService<RuntimeMeterListener>();
		var b = provider.GetRequiredService<RuntimeMeterListener>();

		Assert.Same(a, b);
		Assert.Contains(provider.GetServices<IHostedService>(), s => ReferenceEquals(s, a));
	}

	/// <summary>
	///     The accumulator's memory is fixed regardless of how many distinct tag values are recorded.
	///     error.type carries arbitrary exception type names, so keying anything by it would be
	///     unbounded cardinality; the totals are aggregated instead.
	/// </summary>
	/// <remarks>
	///     Boundedness is asserted by measuring allocation rather than by inspecting a count, because a
	///     count can only report what the implementation chose to report. Ten thousand measurements with
	///     ten thousand distinct tag values allocate nothing at all, which is reachable only if no
	///     per-tag storage exists; storing tag values in any collection would allocate on the first
	///     sight of each new key.
	///
	///     Feeds measurements through <see cref="RuntimeMeterListener.RecordForTest(string, long, ReadOnlySpan{KeyValuePair{string, object}})" />
	///     directly rather than starting the real listener: a started listener attaches to the process-wide
	///     <c>System.Runtime</c> meter, and any exception thrown anywhere else in the test process while
	///     it is running would add to the count this test asserts an exact value for.
	/// </remarks>
	[Fact]
	public void TheAccumulatorDoesNotGrowWithDistinctTagValues()
	{
		var listener = new RuntimeMeterListener();

		for (var i = 0; i < 10_000; i++)
			listener.RecordForTest("dotnet.exceptions", 1, [new KeyValuePair<string, object?>("error.type", $"Type{i}")]);

		Assert.Equal(10_000, listener.ExceptionsThrown);

		var tags = new KeyValuePair<string, object?>[10_000];
		for (var i = 0; i < tags.Length; i++) tags[i] = new KeyValuePair<string, object?>("error.type", $"Probe{i}");
		listener.RecordForTest("dotnet.exceptions", 1, [tags[0]]);

		var before = GC.GetAllocatedBytesForCurrentThread();
		foreach (var tag in tags) listener.RecordForTest("dotnet.exceptions", 1, [tag]);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public async Task StoppingTheHostDisposesTheListener()
	{
		var listener = new RuntimeMeterListener();
		await listener.StartAsync(default);

		await listener.StopAsync(default);

		Assert.True(listener.Disposed);
	}

	[Fact]
	public void ActiveRequestsTracksTheSumOfRecordedDeltasIgnoringTags()
	{
		var listener = new RuntimeMeterListener();

		listener.RecordForTest("http.server.active_requests", 1, [new KeyValuePair<string, object?>("http.route", "/a")]);
		listener.RecordForTest("http.server.active_requests", 1, [new KeyValuePair<string, object?>("http.route", "/b")]);
		listener.RecordForTest("http.server.active_requests", -1, []);

		Assert.Equal(1, listener.ActiveRequests);
	}

	[Fact]
	public void RequestDurationCountsFailuresByErrorTagPresenceAndResetsOnSnapshot()
	{
		var listener = new RuntimeMeterListener();

		listener.RecordForTest("http.server.request.duration", 0.01, []);
		listener.RecordForTest("http.server.request.duration", 0.02, [new KeyValuePair<string, object?>("error.type", "500")]);

		Assert.Equal(2, listener.RequestsCompleted);
		Assert.Equal(1, listener.RequestsFailed);

		var snapshot = listener.SnapshotRequestDuration();
		Assert.Equal(2, snapshot.Count);

		var afterReset = listener.SnapshotRequestDuration();
		Assert.Equal(0, afterReset.Count);
	}
}
