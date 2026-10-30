using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Basil.Testing.Clients;

/// <summary>
///     Behaves like a desktop IRC client or a bot over a real socket: negotiates capabilities, signs in with a key
///     (<c>PASS</c> or SASL PLAIN), sends commands and reads the server's lines. The lines are kept as the server wrote
///     them, so tests compare wire text, not parsed objects.
/// </summary>
public sealed class IrcClient : IDisposable
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private readonly List<string> _received = [];
	private readonly TcpClient _tcp = new();
	private StreamReader? _reader;
	private StreamWriter? _writer;
	private int _barriers;

	/// <summary>Gets every line received so far, in order.</summary>
	public IReadOnlyList<string> Received => _received;

	/// <summary>Gets the server name seen in the lines (<c>irc.</c> plus the domain).</summary>
	public string Server { get; private set; } = string.Empty;

	/// <summary>Connects to the IRC port of a running server.</summary>
	/// <param name="host">The server.</param>
	/// <param name="localAddress">The address the socket calls from, or the one the system picks when omitted.</param>
	/// <returns>A connected client that has sent nothing.</returns>
	public static async Task<IrcClient> ConnectAsync(BasilHost host, IPAddress? localAddress = null)
	{
		var client = new IrcClient { Server = $"irc.{host.Domain}" };
		await client.ConnectAsync("127.0.0.1", host.IrcPort, localAddress);
		return client;
	}

	/// <summary>Connects to an address.</summary>
	/// <param name="host">The address to connect to.</param>
	/// <param name="port">The port to connect to.</param>
	/// <param name="localAddress">The address the socket calls from, or the one the system picks when omitted.</param>
	public async Task ConnectAsync(string host, int port, IPAddress? localAddress = null)
	{
		if (localAddress is not null) _tcp.Client.Bind(new IPEndPoint(localAddress, 0));
		await _tcp.ConnectAsync(host, port);
		var stream = _tcp.GetStream();
		_reader = new StreamReader(stream, Encoding.UTF8);
		_writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
	}

	/// <summary>Sends one line.</summary>
	public Task SendAsync(string line)
	{
		return _writer!.WriteLineAsync(line);
	}

	/// <summary>Reads one line, or <see langword="null" /> when the server closed the connection.</summary>
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

	/// <summary>Reads lines until one matches.</summary>
	/// <param name="match">What the wanted line looks like.</param>
	/// <returns>The matching line.</returns>
	/// <exception cref="InvalidOperationException">The connection closed first.</exception>
	public async Task<string> ReadUntilAsync(Func<string, bool> match)
	{
		while (true)
		{
			var line = await ReadLineAsync() ??
			           throw new InvalidOperationException($"The server closed the connection. Received:\n{Transcript}");
			if (match(line)) return line;
		}
	}

	/// <summary>Reads lines until one has the given command or numeric, ignoring the prefix and the tags.</summary>
	/// <param name="command">The command or the three-digit numeric.</param>
	public Task<string> ExpectAsync(string command)
	{
		return ReadUntilAsync(line => CommandOf(line) == command);
	}

	/// <summary>Returns the command or numeric of a line, ignoring the tags and the prefix.</summary>
	public static string CommandOf(string line)
	{
		var rest = line;
		if (rest.StartsWith('@')) rest = rest[(rest.IndexOf(' ') + 1)..];
		if (rest.StartsWith(':')) rest = rest[(rest.IndexOf(' ') + 1)..];
		var end = rest.IndexOf(' ');
		return end < 0 ? rest : rest[..end];
	}

	/// <summary>
	///     Waits until the server has handled everything sent before and delivered everything queued so far: sends a
	///     <c>PING</c> and reads up to its <c>PONG</c>. Use it to assert that something did <em>not</em> arrive.
	/// </summary>
	/// <returns>The lines that arrived while waiting, excluding the <c>PONG</c>.</returns>
	public async Task<IReadOnlyList<string>> BarrierAsync()
	{
		var start = _received.Count;
		var token = $"barrier-{++_barriers}";
		await SendAsync($"PING {token}");
		await ReadUntilAsync(line => CommandOf(line) == "PONG" && line.EndsWith(token, StringComparison.Ordinal));
		return [.. _received.Skip(start).SkipLast(1)];
	}

	/// <summary>Signs in the way an old client or bot does: <c>PASS</c>, <c>NICK</c>, <c>USER</c>, then waits for the welcome.</summary>
	/// <param name="nick">The nickname.</param>
	/// <param name="key">The IRC key.</param>
	/// <returns>The <c>001</c> line.</returns>
	public async Task<string> RegisterAsync(string nick, string key)
	{
		await SendAsync($"PASS {key}");
		await SendAsync($"NICK {nick}");
		await SendAsync($"USER {nick} 0 * :{nick}");
		return await ExpectAsync("001");
	}

	/// <summary>Signs in with <c>PASS</c> after requesting capabilities, then waits for the welcome.</summary>
	/// <param name="nick">The nickname.</param>
	/// <param name="key">The IRC key.</param>
	/// <param name="caps">The capabilities to request, separated by spaces.</param>
	/// <returns>The <c>001</c> line.</returns>
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

	/// <summary>Signs in with capability negotiation and SASL PLAIN.</summary>
	/// <param name="nick">The nickname.</param>
	/// <param name="account">The SASL authentication identity.</param>
	/// <param name="key">The IRC key, used as the SASL password.</param>
	/// <param name="caps">The capabilities to request besides <c>sasl</c>.</param>
	/// <returns>The line that ended the exchange: <c>903</c> or <c>904</c>.</returns>
	public async Task<string> RegisterWithSaslAsync(string nick, string account, string key, string caps = "")
	{
		await SendAsync("CAP LS 302");
		await ReadUntilAsync(line => line.Contains(" LS ", StringComparison.Ordinal));
		await SendAsync($"CAP REQ :sasl {caps}".TrimEnd());
		await ReadUntilAsync(line => line.Contains(" ACK ", StringComparison.Ordinal) || line.Contains(" NAK ", StringComparison.Ordinal));
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

	/// <summary>Discards the lines received so far.</summary>
	public void Clear()
	{
		_received.Clear();
	}

	/// <summary>Gets the received lines as text, for failure messages.</summary>
	public string Transcript => string.Join('\n', _received);

	public void Dispose()
	{
		_tcp.Dispose();
	}
}
