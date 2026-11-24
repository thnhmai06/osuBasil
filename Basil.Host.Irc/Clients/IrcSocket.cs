using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Basil.Host.Irc.Support;
using Basil.Host.Irc.Transport;
using Basil.Protocol.Irc;
using Basil.Protocol.Irc.Models;

namespace Basil.Host.Irc.Clients;

internal sealed class IrcSocket(TcpClient tcp, IPAddress address, IrcOptions options, TimeProvider clock)
{
	public const int MaxQueuedLines = 4096;

	public const int MaxQueuedCharacters = 1024 * 1024;

	private readonly ConcurrentDictionary<string, byte> _caps = new(StringComparer.Ordinal);
	private readonly Lock _gate = new();

	private readonly Channel<string> _lines = Channel.CreateBounded<string>(
		new BoundedChannelOptions(MaxQueuedLines) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

	private List<Action<IrcSocket>>? _held = [];

	private int _queuedCharacters;

	public IPAddress Address { get; } = address;

	public string Label { get; } = Guid.NewGuid().ToString("N");

	public Session? Session { get; set; }

	public string? RequestedNick { get; set; }

	public string? Pass { get; set; }

	public bool UserGiven { get; set; }

	public bool Negotiating { get; set; }

	public StringBuilder? Sasl { get; set; }

	public bool SaslDone { get; set; }

	public bool Registered { get; set; }

	public string Nick => Session is { } connection ? IrcView.Nick(connection.Record) : "*";

	public string ServerName => options.ServerName;

	public string Domain => options.Domain;

	public DateTimeOffset Now => clock.GetUtcNow();

	public bool Quit { get; set; }

	public IEnumerable<string> Caps => _caps.Select(pair => pair.Key);

	public bool Has(string cap)
	{
		return _caps.ContainsKey(cap);
	}

	public void Set(string cap, bool on)
	{
		if (on) _caps[cap] = 0;
		else _caps.TryRemove(cap, out _);
	}

	public void Send(IrcMessage message)
	{
		try
		{
			var line = message.ToString();
			if (Interlocked.Add(ref _queuedCharacters, line.Length) <= MaxQueuedCharacters &&
			    _lines.Writer.TryWrite(line)) return;

			// The client does not read what it is sent: drop it instead of holding its lines without end.
			_lines.Writer.TryComplete();
			tcp.Close();
		}
		catch (ArgumentException)
		{
			// A part with a line break would split the line: nothing sane to send.
		}
	}

	public void Numeric(IrcNumericReply numeric, params string[] arguments)
	{
		Send(IrcMessage.Numeric(ServerName, numeric, Nick, arguments) with { ForceTrailing = true });
	}

	public void NumericPlain(IrcNumericReply numeric, params string[] arguments)
	{
		Send(IrcMessage.Numeric(ServerName, numeric, Nick, arguments));
	}

	public void Deliver(IrcMessage message)
	{
		Deliver(session => session.Send(message));
	}

	public void Deliver(Action<IrcSocket> write)
	{
		lock (_gate)
		{
			if (_held is { } held) held.Add(write);
			else write(this);
		}
	}

	public void Relay(IrcMessage message, UserRecord? account, DateTimeOffset at)
	{
		if (Has("server-time"))
			message = message.WithTag("time",
				at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
		if (account is not null && Has("account-tag")) message = message.WithTag("account", account.Value.Name);
		Deliver(message);
	}

	public void Ready()
	{
		lock (_gate)
		{
			var held = _held;
			_held = null;
			if (held is not null)
				foreach (var write in held)
					write(this);
		}
	}

	public void Close(string reason)
	{
		Send(IrcMessage.Error(IrcReplies.ClosingLink(options.Domain, reason)));
		_lines.Writer.TryComplete();
	}

	public async Task WriteAsync(Stream stream, CancellationToken ct)
	{
		var buffer = new StringBuilder();
		try
		{
			while (await _lines.Reader.WaitToReadAsync(ct))
			{
				buffer.Clear();
				while (_lines.Reader.TryRead(out var line))
				{
					Interlocked.Add(ref _queuedCharacters, -line.Length);
					buffer.Append(line).Append(IrcWire.LineEnd);
				}

				await stream.WriteAsync(Encoding.UTF8.GetBytes(buffer.ToString()), ct);
			}
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
			                           or OperationCanceledException)
		{
		}
		finally
		{
			tcp.Close();
		}
	}

	public ITimer Every(Action callback, TimeSpan due, TimeSpan period)
	{
		return clock.CreateTimer(_ => callback(), null, due, period);
	}
}