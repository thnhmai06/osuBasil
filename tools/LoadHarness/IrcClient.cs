using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Basil.Protocol.Irc;

namespace LoadHarness;

/// <summary>An IRC client: registers over TLS, joins <c>#osu</c>, chats every few seconds and answers PING.</summary>
internal sealed class IrcClient(Context context, Account account)
{
	private const string Kind = "irc";
	private readonly SemaphoreSlim _writing = new(1, 1);
	private long _probeSentAt;
	private SslStream? _stream;

	public async Task RunAsync(CancellationToken ct)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			var host = context.Options.IrcHost ?? context.Options.Connect ?? context.Options.Domain;
			using var tcp = new TcpClient { NoDelay = true };
			await tcp.ConnectAsync(host, context.Options.IrcPort, ct);
			_stream = new SslStream(tcp.GetStream(), false,
				(_, _, _, _) => context.Options.Insecure);
			await _stream.AuthenticateAsClientAsync(context.Options.Domain);

			var welcome = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
			var reading = ReadAsync(welcome, ct);
			await SendAsync($"{IrcVerb.Pass} {account.Password}", ct);
			await SendAsync($"{IrcVerb.Nick} {account.Name}", ct);
			await SendAsync($"{IrcVerb.User} {account.Name} 0 * :{account.Name}", ct);

			var refused = await welcome.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
			context.Recorder.Record("irc-register", Stopwatch.GetElapsedTime(started), refused, true);
			if (refused is not null) return;

			await SendAsync($"{IrcVerb.Join} #osu", ct);
			for (var n = 1; !ct.IsCancellationRequested; n++)
			{
				await Context.Pause(context.Options.IrcInterval, ct);
				await ChatAsync(n, ct);
				await ProbeAsync(n, ct);
			}

			await reading;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started), Context.Reason(exception), true);
		}
		finally
		{
			_stream?.Dispose();
		}
	}

	// A message has no reply, so its latency is the time to hand it to the connection.
	private async Task ChatAsync(int n, CancellationToken ct)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			await SendAsync($"{IrcVerb.Privmsg} #osu :load {account.Name} {n}", ct);
			context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started));
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started), Context.Reason(exception));
		}
	}

	// A PING that the server must answer measures the round trip of the connection.
	private async Task ProbeAsync(int n, CancellationToken ct)
	{
		var started = Stopwatch.GetTimestamp();
		Volatile.Write(ref _probeSentAt, started);
		try
		{
			await SendAsync($"{IrcVerb.Ping} :probe{n}", ct);
			var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
			while (Volatile.Read(ref _probeSentAt) != 0)
			{
				if (Stopwatch.GetTimestamp() > deadline)
				{
					Volatile.Write(ref _probeSentAt, 0);
					context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started), "pong timeout");
					return;
				}

				await Task.Delay(10, ct);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started), Context.Reason(exception));
		}
	}

	private async Task ReadAsync(TaskCompletionSource<string?> welcome, CancellationToken ct)
	{
		try
		{
			await foreach (var line in IrcLines.ReadAsync(_stream!, ct))
			{
				if (line is null) continue;
				var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length > 0 && parts[0][0] == ':') parts = parts[1..];
				if (parts.Length == 0) continue;

				switch (parts[0])
				{
					case IrcVerb.Ping:
						await SendAsync($"{IrcVerb.Pong} {(parts.Length > 1 ? parts[1] : "")}", ct);
						break;
					case IrcVerb.Pong when line.Contains("probe"):
						if (Interlocked.Exchange(ref _probeSentAt, 0) is var sent and not 0)
							context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(sent));
						break;
					case "001":
						welcome.TrySetResult(null);
						break;
					case IrcVerb.Error:
						welcome.TrySetResult("ERROR");
						break;
					case var numeric when numeric.Length == 3 && numeric[0] is '4' or '5':
						// Before the welcome it refuses the sign-in; after it, one of our commands.
						if (!welcome.TrySetResult($"refused ({numeric})"))
							context.Recorder.Record(Kind, TimeSpan.Zero, $"reply {numeric}");
						break;
				}
			}

			welcome.TrySetResult("connection closed");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			welcome.TrySetResult(Context.Reason(exception));
		}
	}

	private async Task SendAsync(string line, CancellationToken ct)
	{
		var bytes = Encoding.UTF8.GetBytes(line + IrcWire.LineEnd);
		await _writing.WaitAsync(ct);
		try
		{
			await _stream!.WriteAsync(bytes, ct);
			await _stream.FlushAsync(ct);
		}
		finally
		{
			_writing.Release();
		}
	}
}
