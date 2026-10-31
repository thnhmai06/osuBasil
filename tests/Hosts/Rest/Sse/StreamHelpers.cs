namespace Basil.Host.Rest.Tests.Sse;

/// <summary>Drives the hub's clock while a stream reader waits, because events reach the hub asynchronously.</summary>
internal static class StreamHelpers
{
	/// <summary>Advances the clock a second at a time until the next event that is not a snapshot arrives.</summary>
	public static async Task<(string Event, string Data)> NextBundleAsync(this SseReader reader, BasilHost host)
	{
		var read = reader.ReadAsync();
		for (var attempt = 0; attempt < 100; attempt++)
		{
			host.Time.Advance(TimeSpan.FromSeconds(1));
			var finished = await Task.WhenAny(read, Task.Delay(100));
			if (finished == read) return await read ?? throw new InvalidOperationException("The stream ended.");
		}

		throw new TimeoutException("No bundle arrived.");
	}
}