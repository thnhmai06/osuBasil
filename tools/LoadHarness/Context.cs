using System.Net.Sockets;

namespace LoadHarness;

/// <summary>What every client of a run shares: the options, the recorder and the HTTP client that reaches nginx.</summary>
internal sealed class Context
{
	public Context(Options options, Recorder recorder)
	{
		Options = options;
		Recorder = recorder;
		var handler = new SocketsHttpHandler
		{
			MaxConnectionsPerServer = int.MaxValue,
			PooledConnectionLifetime = TimeSpan.FromMinutes(10),
			ConnectCallback = async (context, ct) =>
			{
				var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
				try
				{
					await socket.ConnectAsync(options.Connect ?? context.DnsEndPoint.Host, context.DnsEndPoint.Port, ct);
					return new NetworkStream(socket, true);
				}
				catch
				{
					socket.Dispose();
					throw;
				}
			}
		};
		if (options.Insecure) handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
		Http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
	}

	public Options Options { get; }
	public Recorder Recorder { get; }
	public HttpClient Http { get; }

	public Uri Url(string subdomain, string path)
	{
		var port = Options.Port == 443 ? "" : $":{Options.Port}";
		return new Uri($"https://{subdomain}.{Options.Domain}{port}{path}");
	}

	/// <summary>Waits a pause with up to a tenth of jitter, so clients do not poll in step.</summary>
	public static Task Pause(TimeSpan pause, CancellationToken ct)
	{
		var jitter = 1 + (Random.Shared.NextDouble() - 0.5) / 5;
		return pause == TimeSpan.Zero ? Task.CompletedTask : Task.Delay(pause * jitter, ct);
	}

	/// <summary>Short failure reason: the HTTP status, or the kind of exception.</summary>
	public static string Reason(Exception exception)
	{
		return exception switch
		{
			TaskCanceledException or TimeoutException => "timeout",
			HttpRequestException { StatusCode: { } status } => $"HTTP {(int)status}",
			_ => exception.GetType().Name
		};
	}
}
