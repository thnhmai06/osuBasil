using Basil.Testing.Clients;

namespace Basil.Tests.EndToEnd;

internal static class Streams
{
	public static async Task<(string Event, string Data)> NextAsync(this SseReader reader, BasilHost host)
	{
		return await reader.NextOrEndAsync(host) ?? throw new InvalidOperationException("The stream ended.");
	}

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