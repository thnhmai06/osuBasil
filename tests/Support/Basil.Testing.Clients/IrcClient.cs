using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Basil.Testing.Clients;

public sealed class IrcClient : IDisposable
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private readonly List<string> _received = [];
	private readonly TcpClient _tcp = new();
	private int _barriers;
	private StreamReader? _reader;
	private StreamWriter? _writer;

	public IReadOnlyList<string> Received => _received;

	public string Server { get; private set; } = string.Empty;

	public string Transcript => string.Join('\n', _received);

	public void Dispose()
	{
		_tcp.Dispose();
	}

	public static async Task<IrcClient> ConnectAsync(BasilHost host, IPAddress? localAddress = null)
	{
		var client = new IrcClient { Server = $"irc.{host.Domain}" };
		await client.ConnectAsync("127.0.0.1", host.IrcPort, localAddress);
		return client;
	}

	public async Task ConnectAsync(string host, int port, IPAddress? localAddress = null)
	{
		if (localAddress is not null) _tcp.Client.Bind(new IPEndPoint(localAddress, 0));
		await _tcp.ConnectAsync(host, port);
		var stream = _tcp.GetStream();
		_reader = new StreamReader(stream, Encoding.UTF8);
		_writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
	}

	public Task SendAsync(string line)
	{
		return _writer!.WriteLineAsync(line);
	}

	public async Task<string?> ReadLineAsync(TimeSpan? timeout = null)
	{
		using var cts = new CancellationTokenSource(timeout ?? Patience);
		try
		{
			var line = await _reader!.ReadLineAsync(cts.Token);
			if (line is not null) _received.Add(line);
			return line;
		}
		catch (OperationCanceledException)
		{
			throw new TimeoutException($"No line arrived. Received so far:\n{Transcript}");
		}
		catch (IOException)
		{
			return null;
		}
	}

	public async Task<string> ReadUntilAsync(Func<string, bool> match)
	{
		while (true)
		{
			var line = await ReadLineAsync() ??
			           throw new InvalidOperationException(
				           $"The server closed the connection. Received:\n{Transcript}");
			if (match(line)) return line;
		}
	}

	public Task<string> ExpectAsync(string command)
	{
		return ReadUntilAsync(line => CommandOf(line) == command);
	}

	public static string CommandOf(string line)
	{
		var rest = line;
		if (rest.StartsWith('@')) rest = rest[(rest.IndexOf(' ') + 1)..];
		if (rest.StartsWith(':')) rest = rest[(rest.IndexOf(' ') + 1)..];
		var end = rest.IndexOf(' ');
		return end < 0 ? rest : rest[..end];
	}

	public async Task<IReadOnlyList<string>> BarrierAsync()
	{
		var start = _received.Count;
		var token = $"barrier-{++_barriers}";
		await SendAsync($"PING {token}");
		await ReadUntilAsync(line => CommandOf(line) == "PONG" && line.EndsWith(token, StringComparison.Ordinal));
		return [.. _received.Skip(start).SkipLast(1)];
	}

	public async Task<string> RegisterAsync(string nick, string key)
	{
		await SendAsync($"PASS {key}");
		await SendAsync($"NICK {nick}");
		await SendAsync($"USER {nick} 0 * :{nick}");
		return await ExpectAsync("001");
	}

	public async Task<string> RegisterWithCapsAsync(string nick, string key, string caps)
	{
		await SendAsync("CAP LS 302");
		await SendAsync($"CAP REQ :{caps}");
		await ReadUntilAsync(line => line.Contains(" ACK ", StringComparison.Ordinal));
		await SendAsync($"PASS {key}");
		await SendAsync($"NICK {nick}");
		await SendAsync($"USER {nick} 0 * :{nick}");
		await SendAsync("CAP END");
		return await ExpectAsync("001");
	}

	public async Task<string> RegisterWithSaslAsync(string nick, string account, string key, string caps = "")
	{
		await SendAsync("CAP LS 302");
		await ReadUntilAsync(line => line.Contains(" LS ", StringComparison.Ordinal));
		await SendAsync($"CAP REQ :sasl {caps}".TrimEnd());
		await ReadUntilAsync(line =>
			line.Contains(" ACK ", StringComparison.Ordinal) || line.Contains(" NAK ", StringComparison.Ordinal));
		await SendAsync("AUTHENTICATE PLAIN");
		await ReadUntilAsync(line => line == "AUTHENTICATE +");
		var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{account}\0{key}"));
		await SendAsync($"AUTHENTICATE {payload}");
		var outcome = await ReadUntilAsync(line => CommandOf(line) is "903" or "904");
		await SendAsync($"NICK {nick}");
		await SendAsync($"USER {nick} 0 * :{nick}");
		await SendAsync("CAP END");
		return outcome;
	}

	public void Clear()
	{
		_received.Clear();
	}
}