using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc;

namespace Basil.Host.Irc;

/// <summary>One IRC socket: what the client negotiated, who it signed in as, and the queue of lines to write to it.</summary>
internal sealed class IrcSession(TcpClient tcp, IPAddress address, IrcOptions options, TimeProvider clock)
{
	private readonly ConcurrentDictionary<string, byte> _caps = new(StringComparer.Ordinal);
	private readonly Lock _gate = new();
	private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
	private List<Action<IrcSession>>? _held = [];

	/// <summary>Gets the address of the socket; the only address the server uses and never shows.</summary>
	public IPAddress Address { get; } = address;

	/// <summary>Gets the connection the client signed in as, or <see langword="null" /> before.</summary>
	public Connection? Connection { get; set; }

	/// <summary>Gets or sets the key the socket holds.</summary>
	public string? Key { get; set; }

	/// <summary>Gets or sets the nickname the client asked for.</summary>
	public string? RequestedNick { get; set; }

	/// <summary>Gets or sets the key the client sent with <c>PASS</c>.</summary>
	public string? Pass { get; set; }

	/// <summary>Gets or sets whether <c>USER</c> was received.</summary>
	public bool UserGiven { get; set; }

	/// <summary>Gets or sets whether capability negotiation has started and not ended.</summary>
	public bool Negotiating { get; set; }

	/// <summary>Gets or sets the SASL data received so far, or <see langword="null" /> when no exchange is running.</summary>
	public StringBuilder? Sasl { get; set; }

	/// <summary>Gets or sets whether the client signed in with SASL.</summary>
	public bool SaslDone { get; set; }

	/// <summary>Gets or sets whether the client is registered.</summary>
	public bool Registered { get; set; }

	/// <summary>Gets the nickname shown in replies: the account's once the client is signed in, <c>*</c> before.</summary>
	public string Nick => Connection is { } connection ? IrcView.Nick(connection.Record) : "*";

	/// <summary>Gets the name the server calls itself.</summary>
	public string ServerName => options.ServerName;

	/// <summary>Gets the public domain, which is the host of every user.</summary>
	public string Domain => options.Domain;

	/// <summary>Gets the current time.</summary>
	public DateTimeOffset Now => clock.GetUtcNow();

	/// <summary>Gets or sets whether the client asked to quit.</summary>
	public bool Quit { get; set; }

	/// <summary>Gets the capabilities the client enabled.</summary>
	public IEnumerable<string> Caps => _caps.Keys;

	/// <summary>Checks whether the client enabled a capability.</summary>
	/// <param name="cap">The capability name.</param>
	/// <returns><see langword="true" /> when it is on.</returns>
	public bool Has(string cap)
	{
		return _caps.ContainsKey(cap);
	}

	/// <summary>Turns a capability on or off.</summary>
	/// <param name="cap">The capability name.</param>
	/// <param name="on">Whether it is on.</param>
	public void Set(string cap, bool on)
	{
		if (on) _caps[cap] = 0;
		else _caps.TryRemove(cap, out _);
	}

	/// <summary>Queues a line for the client right away.</summary>
	/// <param name="message">The message to send.</param>
	public void Send(IrcMessage message)
	{
		try
		{
			_lines.Writer.TryWrite(message.ToString());
		}
		catch (ArgumentException)
		{
			// A part with a line break would split the line: nothing sane to send.
		}
	}

	/// <summary>Queues a numeric reply to the client.</summary>
	/// <param name="numeric">The numeric.</param>
	/// <param name="arguments">The parameters after the client's nickname.</param>
	public void Numeric(IrcNumericReply numeric, params string[] arguments)
	{
		Send(IrcMessage.Numeric(ServerName, numeric, Nick, arguments) with { ForceTrailing = true });
	}

	/// <summary>Queues a numeric reply whose last parameter is written as it is, without forcing a colon.</summary>
	/// <param name="numeric">The numeric.</param>
	/// <param name="arguments">The parameters after the client's nickname.</param>
	public void NumericPlain(IrcNumericReply numeric, params string[] arguments)
	{
		Send(IrcMessage.Numeric(ServerName, numeric, Nick, arguments));
	}

	/// <summary>Queues a message that an event produced; it waits while the welcome is still being written.</summary>
	/// <param name="message">The message to send.</param>
	public void Deliver(IrcMessage message)
	{
		Deliver(session => session.Send(message));
	}

	/// <summary>Runs what an event writes to the client once the welcome is written, in the order the events came.</summary>
	/// <param name="write">Writes the lines; it runs at once when the welcome is already written.</param>
	public void Deliver(Action<IrcSession> write)
	{
		lock (_gate)
		{
			if (_held is { } held) held.Add(write);
			else write(this);
		}
	}

	/// <summary>Queues a relayed message with the tags this client asked for.</summary>
	/// <param name="message">The message.</param>
	/// <param name="account">The account that wrote it, or <see langword="null" /> for the server.</param>
	/// <param name="at">When it was written.</param>
	public void Relay(IrcMessage message, UserRecord? account, DateTimeOffset at)
	{
		if (Has("server-time"))
			message = message.WithTag("time", at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
		if (account is not null && Has("account-tag")) message = message.WithTag("account", account.Value.Name);
		Deliver(message);
	}

	/// <summary>Lets the events written so far through; called when the welcome is written.</summary>
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

	/// <summary>Tells the client why the server ends the connection and closes it after the queue is written.</summary>
	/// <param name="reason">The reason, shown in parentheses.</param>
	public void Close(string reason)
	{
		Send(new IrcMessage("ERROR", null, $"Closing Link: {options.Domain} ({reason})"));
		_lines.Writer.TryComplete();
	}

	/// <summary>Writes the queued lines to the socket until the queue is closed.</summary>
	/// <param name="stream">The socket stream.</param>
	/// <param name="cancellationToken">A token that stops writing.</param>
	public async Task WriteAsync(Stream stream, CancellationToken cancellationToken)
	{
		var buffer = new StringBuilder();
		try
		{
			while (await _lines.Reader.WaitToReadAsync(cancellationToken))
			{
				buffer.Clear();
				while (_lines.Reader.TryRead(out var line)) buffer.Append(line).Append("\r\n");
				await stream.WriteAsync(Encoding.UTF8.GetBytes(buffer.ToString()), cancellationToken);
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

	/// <summary>Starts a timer on the server's clock.</summary>
	/// <param name="callback">What to do when it fires.</param>
	/// <param name="due">When it first fires.</param>
	/// <param name="period">How often it fires again, or <see cref="Timeout.InfiniteTimeSpan" />.</param>
	/// <returns>The timer.</returns>
	public ITimer Every(Action callback, TimeSpan due, TimeSpan period)
	{
		return clock.CreateTimer(_ => callback(), null, due, period);
	}
}
