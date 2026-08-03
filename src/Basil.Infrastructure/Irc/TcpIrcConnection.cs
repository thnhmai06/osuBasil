using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Configurations;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Irc;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Irc;

/// <summary>
///     One real TCP IRC client. Owns the socket's read loop (handshake, then PRIVMSG/JOIN/PART/AWAY/
///     PING/QUIT dispatch) and a bounded-channel write pump. <see cref="Send" /> is a non-blocking
///     <c>TryWrite</c>, so a slow or dead client can never stall a broadcast made while another lock
///     is held elsewhere in the chat core.
/// </summary>
/// <remarks>
///     Implements <see cref="IIrcConnection" /> for a real external IRC client. Outgoing messages
///     queue on a bounded outbox that drops the oldest entry when full, honoring the port's
///     non-blocking sending contract. The connection id scopes this connection's log events.
/// </remarks>
/// <param name="client">The accepted TCP socket to read from and write to.</param>
/// <param name="authService">Authenticates the PASS/NICK handshake and builds the session on success.</param>
/// <param name="chatDispatch">Routes registered PRIVMSG traffic into the shared chat core.</param>
/// <param name="channelMembership">Handles JOIN/PART commands and the quit performed at teardown.</param>
/// <param name="channelRegistry">Resolves JOIN/PART channel names to their live sessions.</param>
/// <param name="sessionRegistry">The registry the authenticated session is added to and later removed from.</param>
/// <param name="options">Provides the gateway's display name used in numerics and PINGs.</param>
/// <param name="logger">Logs this connection's lifecycle events.</param>
/// <param name="connectionId">The per-process-unique id assigned to this connection by the listener.</param>
public sealed class TcpIrcConnection(
	TcpClient client,
	IrcAuthenticationService authService,
	ChatDispatchService chatDispatch,
	ChannelMembershipService channelMembership,
	IChannelRegistry channelRegistry,
	IUserSessionRegistry sessionRegistry,
	IOptions<IrcOptions> options,
	ILogger<TcpIrcConnection> logger,
	long connectionId) : IIrcConnection
{
	/// <summary>The interval at which a keepalive PING is sent to a registered client.</summary>
	private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(60);

	/// <summary>
	///     The bounded outbox drained by the writing pump. When full, the oldest queued message is
	///     dropped so a slow client can never block a sender.
	/// </summary>
	private readonly Channel<IrcMessage> _outbox = Channel.CreateBounded<IrcMessage>(
		new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });

	/// <summary>Indicates whether the PASS/NICK handshake has completed and <see cref="User" /> is set.</summary>
	private bool _registered;

	/// <inheritdoc />
	/// <remarks>Null until authentication succeeds.</remarks>
	public UserSession User { get; private set; } = null!;

	/// <inheritdoc />
	/// <remarks>Always <see langword="true" />: this type is the real external IRC transport.</remarks>
	public bool IsExternalIrcClient => true;

	/// <summary>
	///     Enqueues to the bounded outbox with a non-blocking <c>TryWrite</c>.
	///     When the outbox is full, the oldest queued message is dropped rather than this call stalling.
	/// </summary>
	public void Send(IrcMessage message)
	{
		_outbox.Writer.TryWrite(message);
	}

	/// <summary>
	///     Runs the connection until the client closes the socket or <paramref name="cancellationToken" />
	///     is canceled. Starts the writing pump and ping loop, drives the read loop, then on teardown
	///     quits the session channels, and removes it from the session registry.
	/// </summary>
	/// <param name="cancellationToken">A token that stops the connection and triggers teardown.</param>
	/// <returns>A task that completes when the connection has finished its teardown.</returns>
	public async Task RunAsync(CancellationToken cancellationToken)
	{
		using var _ = logger.BeginScope(new Dictionary<string, object> { ["ConnectionId"] = connectionId });

		await using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		await using var writer = new StreamWriter(stream, Encoding.UTF8);
		writer.AutoFlush = true;
		writer.NewLine = "\r\n";

		using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var writePump = PumpWritesAsync(writer, lifetime.Token);
		var pingLoop = PingLoopAsync(lifetime.Token);

		try
		{
			await ReadLoopAsync(reader, lifetime.Token);
		}
		finally
		{
			await lifetime.CancelAsync();
			_outbox.Writer.TryComplete();
			await Task.WhenAll(writePump, pingLoop);

			if (_registered)
			{
				logger.LogInformation("IRC client disconnected: UserId={UserId} Nick={Nick}", User.Id, User.Name);
				channelMembership.Quit(User, "Connection closed");
				sessionRegistry.Remove(User);
			}
		}
	}

	/// <summary>
	///     Reads client lines until the socket closes. Before registration, buffers the PASS and NICK
	///     values and attempts authentication once both are present. After registration, dispatches
	///     each line through <see cref="HandleRegisteredCommandAsync" /> and refreshes the session's
	///     <see cref="UserSession.LastRecvTime" />.
	/// </summary>
	private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		string? nick = null;
		string? pass = null;

		while (!cancellationToken.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line is null) return;

			if (!IrcMessageParser.TryParse(line, out var message) || message is null) continue;

			if (_registered)
			{
				User.LastRecvTime = DateTimeOffset.UtcNow;
				if (!await HandleRegisteredCommandAsync(message, cancellationToken)) return;

				continue;
			}

			switch (message.Command)
			{
				case "PASS":
					pass = message.Params.Count > 0 ? message.Params[0] : null;
					break;
				case "NICK":
					nick = message.Params.Count > 0 ? message.Params[0] : null;
					break;
				case "PING":
					Send(IrcMessageWriter.Pong(message.Params.Count > 0 ? message.Params[0] : ""));
					break;
				// USER's real-name and hostname fields carry nothing Basil needs; PASS+NICK are enough.
			}

			if (!_registered && nick is not null && pass is not null)
				await TryRegisterAsync(nick, pass, cancellationToken);
		}
	}

	/// <summary>Returns false when the connection should close (QUIT).</summary>
	private async Task<bool> HandleRegisteredCommandAsync(IrcMessage message, CancellationToken cancellationToken)
	{
		switch (message.Command)
		{
			case "PRIVMSG" when message.Params.Count >= 2:
				await chatDispatch.SendPrivmsgAsync(User, message.Params[0], message.Params[1], cancellationToken);
				break;

			case "JOIN" when message.Params.Count >= 1:
				var joinTarget = channelRegistry.GetByName(message.Params[0]);
				if (joinTarget is not null) channelMembership.Join(User, joinTarget);
				break;

			case "PART" when message.Params.Count >= 1:
				var partTarget = channelRegistry.GetByName(message.Params[0]);
				if (partTarget is not null) channelMembership.Part(User, partTarget, false);
				break;

			case "AWAY":
				User.AwayMessage = message.Params.Count > 0 ? message.Params[0] : null;
				break;

			case "PING":
				Send(IrcMessageWriter.Pong(message.Params.Count > 0 ? message.Params[0] : ""));
				break;

			case "NICK":
				// "Can I use another username? No." (osu!Bancho IRC FAQ). Nick is fixed at login.
				Send(IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrUnknownCommand,
					User.Name, "NICK", "Changing nickname is not supported"));
				break;

			case "QUIT":
				logger.LogDebug("IRC client sent explicit QUIT: UserId={UserId}", User.Id);
				return false;
		}

		return true;
	}

	/// <summary>
	///     Attempts authentication with the supplied nick and password, sending each reply message
	///     from the outcome, and on success stores the session and marks the connection registered.
	/// </summary>
	private async Task TryRegisterAsync(string nick, string pass, CancellationToken cancellationToken)
	{
		var outcome = await authService.AuthenticateAsync(nick, pass, this, cancellationToken);
		foreach (var reply in outcome.Messages) Send(reply);

		if (!outcome.Success)
		{
			logger.LogInformation("IRC login failed: Nick={Nick}", nick);
			return;
		}

		User = outcome.Session!;
		_registered = true;
		logger.LogInformation("IRC login succeeded: UserId={UserId} Nick={Nick}", User.Id, User.Name);
	}

	/// <summary>Drains the outbox, writing each message to the socket as a CRLF-terminated wire line.</summary>
	private async Task PumpWritesAsync(StreamWriter writer, CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var message in _outbox.Reader.ReadAllAsync(cancellationToken))
				await writer.WriteLineAsync(IrcMessageWriter.Format(message));
		}
		catch (OperationCanceledException)
		{
			// Expected on disconnect/shutdown.
		}
		catch (IOException)
		{
			// The client went away mid-write.
		}
	}

	/// <summary>Sends a keepalive PING to a registered client every <see cref="PingInterval" />.</summary>
	private async Task PingLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(PingInterval, cancellationToken);
				if (_registered) Send(IrcMessageWriter.Ping(options.Value.Name));
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on disconnect/shutdown.
		}
	}
}