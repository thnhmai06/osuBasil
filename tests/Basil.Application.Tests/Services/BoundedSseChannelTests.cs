using System.Net.ServerSentEvents;
using System.Threading.Channels;
using Basil.Application.Services;
using Xunit;

namespace Basil.Application.Tests.Services;

public class BoundedSseChannelTests
{
	[Fact]
	public async Task WriteWithGapMarker_MoreWritesThanCapacityWithNoReader_TerminatesAndStaysAtCapacity()
	{
		var channel = Channel.CreateBounded<SseItem<string>>(4);

		// Regression: the previous eviction loop re-checked Count after writing a gap marker
		// into the very slot it had just freed, so it never converged — this call would spin
		// forever on whichever thread published (a packet-handler thread holding the match
		// lock) once the channel filled with nobody reading. Bound the wait so a regression
		// fails the test instead of hanging the whole run.
		var writes = Task.Run(() =>
		{
			for (var i = 0; i < 4 + 10; i++)
				BoundedSseChannel.WriteWithGapMarker(channel.Writer, channel.Reader, 4, "score", $"payload-{i}");
		});
		var completed = await Task.WhenAny(writes, Task.Delay(TimeSpan.FromSeconds(5))) == writes;

		Assert.True(completed, "WriteWithGapMarker must never spin forever when full and nobody is reading.");
		Assert.Equal(4, channel.Reader.Count);
	}

	[Fact]
	public void WriteWithGapMarker_Overflow_KeepsLatestPayloadAndFlagsOneGap()
	{
		var channel = Channel.CreateBounded<SseItem<string>>(2);

		BoundedSseChannel.WriteWithGapMarker(channel.Writer, channel.Reader, 2, "score", "first");
		BoundedSseChannel.WriteWithGapMarker(channel.Writer, channel.Reader, 2, "score", "second");
		BoundedSseChannel.WriteWithGapMarker(channel.Writer, channel.Reader, 2, "score", "third");

		Assert.True(channel.Reader.TryRead(out var gap));
		Assert.Equal("gap", gap.EventType);
		Assert.True(channel.Reader.TryRead(out var latest));
		Assert.Equal("third", latest.Data);
		Assert.False(channel.Reader.TryRead(out _));
	}

	[Fact]
	public void WriteWithGapMarker_UnderCapacity_WritesNoGap()
	{
		var channel = Channel.CreateBounded<SseItem<string>>(4);

		BoundedSseChannel.WriteWithGapMarker(channel.Writer, channel.Reader, 4, "score", "only");

		Assert.True(channel.Reader.TryRead(out var item));
		Assert.Equal("score", item.EventType);
		Assert.False(channel.Reader.TryRead(out _));
	}
}