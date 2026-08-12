using Basil.LoadTests.Client;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Polls <c>GET /</c> on the bancho host until it returns the literal liveness string <c>cho</c>,
///     shared by every owned <see cref="IServerHost" /> so "is the server up" is answered one way.
/// </summary>
public static class ServerReadinessProbe
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

	/// <summary>Polls until the server answers or <paramref name="timeout" /> elapses.</summary>
	/// <returns>The time elapsed until the first successful response.</returns>
	/// <exception cref="TimeoutException">The server never became healthy within <paramref name="timeout" />.</exception>
	public static async Task<TimeSpan> WaitAsync(BasilHttpClientFactory clientFactory, TimeSpan timeout,
		CancellationToken cancellationToken = default)
	{
		var started = DateTimeOffset.UtcNow;
		using var client = clientFactory.CreateClient();

		while (DateTimeOffset.UtcNow - started < timeout)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				using var response = await client.GetAsync(clientFactory.BuildUri("c", "/"), cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					var body = await response.Content.ReadAsStringAsync(cancellationToken);
					if (body == "cho") return DateTimeOffset.UtcNow - started;
				}
			}
			catch (HttpRequestException)
			{
				// Server not accepting connections yet; keep polling.
			}
			catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				// Request-level timeout while the server is still starting; keep polling.
			}

			await Task.Delay(PollInterval, cancellationToken);
		}

		throw new TimeoutException($"Server did not become healthy within {timeout}.");
	}
}