using Basil.Testing.Clients;

namespace Basil.Host.Tests.EndToEnd;

/// <summary>Reads live streams while the fake clock of the server advances the hub's second.</summary>
internal static class Streams
{
	/// <summary>Reads the next event, advancing the clock until the hub has bundled one.</summary>
	public static async Task<(string Event, string Data)> NextAsync(this SseReader reader, BasilHost host)
	{
		return await reader.NextOrEndAsync(host) ?? throw new InvalidOperationException("The stream ended.");
	}

	/// <summary>Reads the next event, or <see langword="null" /> when the stream ended.</summary>
	public static async Task<(string Event, string Data)?> NextOrEndAsync(this SseReader reader, BasilHost host)
	{
		var read = reader.ReadAsync();
		for (var attempt = 0; attempt < 100; attempt++)
		{
			host.Time.Advance(TimeSpan.FromSeconds(1));
			if (await Task.WhenAny(read, Task.Delay(50)) == read) return await read;
		}

		throw new TimeoutException("No event arrived.");
	}
}
