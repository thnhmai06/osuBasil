using Basil.Host.Rest.Sse;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Sse;

/// <summary>Waits for the asynchronous delivery of events to a root, so a test advances the clock only afterwards.</summary>
internal static class Pending
{
	/// <summary>Waits until the root has collected at least one event.</summary>
	public static async Task WaitAsync(BasilHost host, string key)
	{
		var hub = host.Services.GetRequiredService<SseHub>();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		while (hub.Pending(key) == 0)
		{
			timeout.Token.ThrowIfCancellationRequested();
			await Task.Yield();
		}
	}
}
