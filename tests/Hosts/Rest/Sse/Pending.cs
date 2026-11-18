using Basil.Host.Rest.Sse;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests.Sse;

internal static class Pending
{
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