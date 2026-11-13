using Basil.Host.Cli.Dashboard.Data;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>What the dashboard keeps of the log and of the chat, and hands out in order.</summary>
public sealed class FeedTests
{
	private static LogLine Line(string text)
	{
		return new LogLine(DateTimeOffset.UnixEpoch, LogLevel.Information, "x", text);
	}

	[Fact]
	public void LogBuffer_Since_ReturnsOnlyWhatIsNew()
	{
		var buffer = new LogBuffer(TimeProvider.System, 10);
		buffer.Add(Line("a"));
		buffer.Add(Line("b"));

		var first = buffer.Since(0, out var total);
		buffer.Add(Line("c"));
		var next = buffer.Since(total, out var newTotal);

		Assert.Equal(["a", "b"], first.Select(l => l.Message));
		Assert.Equal(["c"], next.Select(l => l.Message));
		Assert.Equal(3, newTotal);
	}

	[Fact]
	public void LogBuffer_Since_NeverReturnsLinesThatWereDropped()
	{
		var buffer = new LogBuffer(TimeProvider.System, 2);
		foreach (var text in new[] { "a", "b", "c", "d" }) buffer.Add(Line(text));

		Assert.Equal(["c", "d"], buffer.Since(0, out _).Select(l => l.Message));
	}

	[Fact]
	public void ChatFeed_KeepsEachChannelApartAndBounded()
	{
		var feed = new ChatFeed(2);
		foreach (var text in new[] { "1", "2", "3" })
			feed.Add(new ChatLine(DateTimeOffset.UnixEpoch, "#osu", "u", text));
		feed.Add(new ChatLine(DateTimeOffset.UnixEpoch, "#mp_1", "u", "x"));

		Assert.Equal(["#mp_1", "#osu"], feed.Channels());
		Assert.Equal(["2", "3"], feed.Since("#osu", 0, out var total).Select(l => l.Text));
		Assert.Equal(3, total);
		Assert.Empty(feed.Since("#osu", total, out _));
		Assert.Empty(feed.Since("#none", 0, out _));
	}

	[Fact]
	public void ChatFeed_ForgetsTheQuietestChannelBeyondItsLimit()
	{
		var feed = new ChatFeed(5, 2);
		feed.Add(new ChatLine(DateTimeOffset.UnixEpoch, "#a", "u", "1"));
		feed.Add(new ChatLine(DateTimeOffset.UnixEpoch, "#b", "u", "1"));
		feed.Add(new ChatLine(DateTimeOffset.UnixEpoch, "#a", "u", "2"));
		feed.Add(new ChatLine(DateTimeOffset.UnixEpoch, "#c", "u", "1"));

		Assert.Equal(["#a", "#c"], feed.Channels());
	}
}
