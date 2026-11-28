using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Basil.Protocol.Irc;
using Basil.Protocol.Irc.Models;

namespace Basil.Tests.Shared.Clients.Irc;

/// <summary>
///     An IRC client over TLS: registers with the account password, answers PING, and hands every received line to
///     whoever is waiting for it.
/// </summary>
/// <remarks>
///     Lines are not kept: to see a reply, register the expectation with <see cref="Expect" /> before sending the command
///     that causes it.
///     The proxy header of the connection is added by the front, not by this client.
/// </remarks>
/// <param name="endpoint">Where the server is reached; the TLS name is <c>irc.</c> of its domain.</param>
/// <param name="port">The IRC TLS port, usually 6697.</param>
/// <param name="name">The account name; spaces are written as underscores in the nickname.</param>
/// <param name="password">The account password.</param>
public sealed class IrcClient(Endpoint endpoint, int port, string name, string password) : IAsyncDisposable
{
	private readonly CancellationTokenSource _stop = new();
	private readonly List<IrcExpectation> _waiting = [];
	private readonly SemaphoreSlim _writing = new(1, 1);
	private Task _reading = Task.CompletedTask;
	private SslStream? _stream;
	private TcpClient? _tcp;

	/// <summary>Gets the nickname the client registers with.</summary>
	public string Nick => name.Replace(' ', '_');

	/// <summary>Quits and closes the connection.</summary>
	public async ValueTask DisposeAsync()
	{
		try
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
			if (_stream is not null) await SendAsync(new IrcMessage(IrcVerb.Quit), timeout.Token);
		}
		catch (Exception exception) when (exception is IOException or OperationCanceledException
			                                  or ObjectDisposedException)
		{
			// The connection is gone already.
		}

		await _stop.CancelAsync();
		if (_stream is not null) await _stream.DisposeAsync();
		_tcp?.Dispose();
		try
		{
			await _reading;
		}
		catch (Exception exception) when (exception is IOException or OperationCanceledException
			                                  or ObjectDisposedException)
		{
			// Closing the stream ends the reading.
		}

		_stop.Dispose();
		_writing.Dispose();
	}

	/// <summary>Connects and registers.</summary>
	/// <param name="timeout">How long to wait for the welcome or a refusal.</param>
	/// <param name="ct">Stops the connecting.</param>
	/// <returns>
	///     Whether the server welcomed the client, with the line that decided it; the line is <see langword="null" />
	///     when the wait ran out.
	/// </returns>
	public async Task<IrcRegistration> ConnectAsync(TimeSpan timeout, CancellationToken ct)
	{
		_tcp = new TcpClient { NoDelay = true };
		await _tcp.ConnectAsync(endpoint.Connect, port, ct);
		_stream = new SslStream(_tcp.GetStream(), false, (_, _, _, _) => true);
		await _stream.AuthenticateAsClientAsync(endpoint.HostOf("irc"));
		_reading = Task.Run(() => ReadAsync(_stop.Token), CancellationToken.None);

		using var decided = Expect(line => IsNumeric(line, IrcNumericReply.RplWelcome) ||
		                                   line.Command == IrcVerb.Error || IsRefusal(line));
		await SendAsync(new IrcMessage(IrcVerb.Pass, null, password), ct);
		await SendAsync(new IrcMessage(IrcVerb.Nick, null, Nick), ct);
		await SendAsync(new IrcMessage(IrcVerb.User, null, Nick, "0", "*", Nick), ct);
		var line = await decided.WaitAsync(timeout, ct);
		return new IrcRegistration(line is not null && IsNumeric(line, IrcNumericReply.RplWelcome), line);
	}

	/// <summary>Registers interest in a line; the expectation is met by the first received line the test accepts.</summary>
	/// <param name="accepts">Decides whether a received line is the one wanted.</param>
	public IrcExpectation Expect(Func<IrcMessage, bool> accepts)
	{
		// A line the server turns away is answered by a notice only, so the notice ends every wait.
		var expectation = new IrcExpectation(line => IsTurnedAway(line) || accepts(line), Forget);
		lock (_waiting)
		{
			_waiting.Add(expectation);
		}

		return expectation;
	}

	/// <summary>Sends a line.</summary>
	public async Task SendAsync(IrcMessage message, CancellationToken ct)
	{
		var bytes = Encoding.UTF8.GetBytes(message.ToString(formatProvider: CultureInfo.InvariantCulture) +
		                                   IrcWire.LineEnd);
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

	/// <summary>Sends a line and waits for the line that answers it, registering the wait first so no answer is missed.</summary>
	/// <returns>The answering line, or <see langword="null" /> when the wait ran out.</returns>
	public async Task<IrcMessage?> RequestAsync(IrcMessage message, Func<IrcMessage, bool> answer, TimeSpan timeout,
		CancellationToken ct)
	{
		using var expectation = Expect(answer);
		await SendAsync(message, ct);
		return await expectation.WaitAsync(timeout, ct);
	}

	/// <summary>Sends a message to a channel or a user.</summary>
	public Task PrivmsgAsync(string target, string text, CancellationToken ct)
	{
		return SendAsync(new IrcMessage(IrcVerb.Privmsg, null, target, text), ct);
	}

	/// <summary>Joins a channel.</summary>
	public Task JoinAsync(string channel, CancellationToken ct)
	{
		return SendAsync(new IrcMessage(IrcVerb.Join, null, channel), ct);
	}

	/// <summary>Leaves a channel.</summary>
	public Task PartAsync(string channel, CancellationToken ct)
	{
		return SendAsync(new IrcMessage(IrcVerb.Part, null, channel), ct);
	}

	/// <summary>Asks for the names in a channel and waits for the end of the list.</summary>
	public Task<IrcMessage?> NamesAsync(string channel, TimeSpan timeout, CancellationToken ct)
	{
		return RequestAsync(new IrcMessage(IrcVerb.Names, null, channel),
			line => IsEndOf(line, IrcNumericReply.RplEndOfNames), timeout, ct);
	}

	/// <summary>Asks who matches a mask and waits for the end of the list.</summary>
	public Task<IrcMessage?> WhoAsync(string mask, TimeSpan timeout, CancellationToken ct)
	{
		return RequestAsync(new IrcMessage(IrcVerb.Who, null, mask),
			line => IsEndOf(line, IrcNumericReply.RplEndOfWho), timeout, ct);
	}

	/// <summary>Asks about a user and waits for the end of the answer.</summary>
	public Task<IrcMessage?> WhoisAsync(string nick, TimeSpan timeout, CancellationToken ct)
	{
		return RequestAsync(new IrcMessage(IrcVerb.Whois, null, nick),
			line => IsEndOf(line, IrcNumericReply.RplEndOfWhoIs), timeout, ct);
	}

	/// <summary>Asks for the channel list and waits for its end.</summary>
	public Task<IrcMessage?> ListAsync(TimeSpan timeout, CancellationToken ct)
	{
		return RequestAsync(new IrcMessage(IrcVerb.List), line => IsEndOf(line, IrcNumericReply.RplListEnd), timeout,
			ct);
	}

	/// <summary>Asks for the topic of a channel and waits for the answer.</summary>
	public Task<IrcMessage?> TopicAsync(string channel, TimeSpan timeout, CancellationToken ct)
	{
		return RequestAsync(new IrcMessage(IrcVerb.Topic, null, channel),
			line => IsNumeric(line, IrcNumericReply.RplTopic) || IsNumeric(line, IrcNumericReply.RplNoTopic), timeout,
			ct);
	}

	/// <summary>Marks the client away with a text, or back when the text is <see langword="null" />, and waits for the answer.</summary>
	public Task<IrcMessage?> AwayAsync(string? text, TimeSpan timeout, CancellationToken ct)
	{
		return RequestAsync(text is null ? new IrcMessage(IrcVerb.Away) : new IrcMessage(IrcVerb.Away, null, text),
			line => IsNumeric(line, IrcNumericReply.RplNowAway) || IsNumeric(line, IrcNumericReply.RplUnaway), timeout,
			ct);
	}

	private async Task ReadAsync(CancellationToken ct)
	{
		var failure = new IOException("The IRC connection closed.");
		try
		{
			await foreach (var text in IrcLines.ReadAsync(_stream!, ct))
			{
				if (text is null || !IrcMessage.TryParse(text, CultureInfo.InvariantCulture, out var line)) continue;

				if (line.Command == IrcVerb.Ping)
					await SendAsync(new IrcMessage(IrcVerb.Pong, null, line.Parameters), ct);
				Deliver(line);
			}
		}
		catch (Exception exception) when (exception is IOException or OperationCanceledException
			                                  or ObjectDisposedException)
		{
			failure = new IOException("The IRC connection closed.", exception);
		}
		finally
		{
			Close(failure);
		}
	}

	private void Deliver(IrcMessage line)
	{
		IrcExpectation[] met;
		lock (_waiting)
		{
			met = [.. _waiting.Where(waiting => waiting.Accepts(line))];
			foreach (var waiting in met) _waiting.Remove(waiting);
		}

		foreach (var waiting in met) waiting.Meet(line);
	}

	private void Close(Exception failure)
	{
		IrcExpectation[] open;
		lock (_waiting)
		{
			open = [.. _waiting];
			_waiting.Clear();
		}

		foreach (var waiting in open) waiting.Fail(failure);
	}

	private void Forget(IrcExpectation expectation)
	{
		lock (_waiting)
		{
			_waiting.Remove(expectation);
		}
	}

	// A notice from the server itself (no user mask in its prefix) addressed to this connection.
	private bool IsTurnedAway(IrcMessage line)
	{
		return line is { Command: IrcVerb.Notice, Prefix: { } prefix, Parameters.Count: >= 2 } &&
		       !prefix.Contains('!') && string.Equals(line.Parameters[0], Nick, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsNumeric(IrcMessage line, IrcNumericReply numeric)
	{
		return line.Command == ((int)numeric).ToString("D3");
	}

	private static bool IsEndOf(IrcMessage line, IrcNumericReply numeric)
	{
		return IsNumeric(line, numeric) || IsRefusal(line);
	}

	// A numeric from 400 to 599 refuses a command, or the sign-in before the welcome.
	private static bool IsRefusal(IrcMessage line)
	{
		return line.Command is ['4' or '5', _, _] && ushort.TryParse(line.Command, out _);
	}
}

/// <summary>The outcome of registering an IRC client.</summary>
/// <param name="Welcomed">Whether the server welcomed the client.</param>
/// <param name="Line">
///     The line that decided it: the welcome, a refusing numeric or <c>ERROR</c>; <see langword="null" />
///     when the wait ran out.
/// </param>
public sealed record IrcRegistration(bool Welcomed, IrcMessage? Line);

/// <summary>A wait for one received line, registered before the command that causes it is sent.</summary>
public sealed class IrcExpectation : IDisposable
{
	private readonly Func<IrcMessage, bool> _accepts;
	private readonly Action<IrcExpectation> _forget;
	private readonly TaskCompletionSource<IrcMessage> _met = new(TaskCreationOptions.RunContinuationsAsynchronously);

	internal IrcExpectation(Func<IrcMessage, bool> accepts, Action<IrcExpectation> forget)
	{
		_accepts = accepts;
		_forget = forget;
	}

	/// <summary>Stops waiting.</summary>
	public void Dispose()
	{
		_forget(this);
	}

	/// <summary>Waits for the line.</summary>
	/// <param name="timeout">How long to wait.</param>
	/// <param name="ct">Stops the wait.</param>
	/// <returns>The line, or <see langword="null" /> when the wait ran out.</returns>
	/// <exception cref="IOException">The connection closed before the line came.</exception>
	public async Task<IrcMessage?> WaitAsync(TimeSpan timeout, CancellationToken ct)
	{
		try
		{
			return await _met.Task.WaitAsync(timeout, ct);
		}
		catch (TimeoutException)
		{
			return null;
		}
	}

	internal bool Accepts(IrcMessage line)
	{
		return _accepts(line);
	}

	internal void Meet(IrcMessage line)
	{
		_met.TrySetResult(line);
	}

	internal void Fail(Exception failure)
	{
		_met.TrySetException(failure);
	}
}