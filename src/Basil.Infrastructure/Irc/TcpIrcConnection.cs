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
///     One real TCP IRC client. Owns the socket's read loop (handshake, then chat, membership,
///     query, and keepalive dispatch) and a bounded-channel write pump. <see cref="Send" /> is a non-blocking
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
/// <param name="channelMembership">Handles JOIN/PART commands and builds the LIST reply.</param>
/// <param name="ircQueries">Builds the replies to the read-only query commands.</param>
/// <param name="channelRegistry">Resolves channel names to their live sessions.</param>
/// <param name="playerLogout">Performs the shared teardown when this connection disconnects.</param>
/// <param name="options">Provides the gateway's display name used in numerics and PINGs.</param>
/// <param name="logger">Logs this connection's lifecycle events.</param>
/// <param name="connectionId">The per-process-unique id assigned to this connection by the listener.</param>
public sealed class TcpIrcConnection(
	TcpClient client,
	IrcAuthenticationService authService,
	ChatDispatchService chatDispatch,
	ChannelMembershipService channelMembership,
	IrcQueryService ircQueries,
	IChannelRegistry channelRegistry,
	PlayerLogoutService playerLogout,
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

	/// <summary>Gets the authenticated session, or null until authentication succeeds.</summary>
	public IrcSession User { get; private set; } = null!;

	/// <inheritdoc />
	UserSession IIrcConnection.User => User;

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
				// CancellationToken.None: teardown must run to completion even though the connection's
				// own lifetime token is what triggered this disconnect in the first place.
				await playerLogout.LogoutAsync(User, CancellationToken.None);
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
					if (string.IsNullOrWhiteSpace(nick))
					{
						nick = null;
						Send(IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrErroneousNickname,
							"*", "Erroneous nickname"));
					}

					break;
				case "PING":
					Send(IrcMessageWriter.Pong(message.Params.Count > 0 ? message.Params[0] : ""));
					break;
				case "CAP":
					HandleCap(message);
					break;
				case "USER" or "PONG" or "AUTHENTICATE":
					// USER's real-name and hostname fields carry nothing Basil needs; PASS+NICK are
					// enough. PONG answers a keepalive, and SASL is never offered — none needs a reply.
					break;
				case "QUIT":
					logger.LogDebug("IRC client quit before registering");
					return;
				default:
					Send(IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrNotRegistered, "*",
						"You have not registered"));
					break;
			}

			if (!_registered && nick is not null && pass is not null)
				await TryRegisterAsync(nick, pass, cancellationToken);
		}
	}

	/// <summary>Returns false when the connection should close (QUIT).</summary>
	private async Task<bool> HandleRegisteredCommandAsync(IrcMessage message, CancellationToken cancellationToken)
	{
		var first = message.Params.Count > 0 ? message.Params[0] : null;
		var second = message.Params.Count > 1 ? message.Params[1] : null;

		switch (message.Command)
		{
			case "PRIVMSG" or "NOTICE" when first is null || second is null:
			case "JOIN" or "PART" or "TOPIC" or "MODE" or "WHOIS" or "ISON" when first is null:
				Send(Numeric(IrcNumeric.ErrNeedMoreParams, message.Command, "Not enough parameters"));
				break;

			case "PRIVMSG":
				if (EnsureCanSendTo(first))
					await chatDispatch.SendPrivmsgAsync(User, first, second, cancellationToken);
				break;

			case "NOTICE":
				if (EnsureCanSendTo(first))
					await chatDispatch.SendNoticeAsync(User, first, second, cancellationToken);
				break;

			case "JOIN":
				if (channelRegistry.GetByName(first) is not { } joinTarget)
					Send(Numeric(IrcNumeric.ErrNoSuchChannel, first, "No such channel"));
				else if (!channelMembership.Join(User, joinTarget))
					Send(Numeric(IrcNumeric.ErrInviteOnlyChan, first, "Cannot join channel (no permission)"));
				break;

			case "PART":
				var partTarget = channelRegistry.GetByName(first);
				if (partTarget is not null) channelMembership.Part(User, partTarget, false);
				break;

			case "LIST":
				SendAll(channelMembership.BuildListReply(User, first));
				break;

			case "NAMES":
				SendAll(ircQueries.BuildNamesReply(User, first));
				break;

			case "TOPIC":
				Send(ircQueries.BuildTopicReply(User, first, second));
				break;

			case "MODE":
				// Only channel modes are reported; Basil has no per-user mode to read or set.
				if (first.StartsWith('#')) Send(ircQueries.BuildChannelModeReply(User, first, second));
				break;

			case "WHO":
				SendAll(ircQueries.BuildWhoReply(User, first ?? "*"));
				break;

			case "WHOIS":
				SendAll(ircQueries.BuildWhoisReply(User, first));
				break;

			case "ISON":
				// A client may spell the list as separate parameters or as one trailing parameter.
				Send(ircQueries.BuildIsonReply(User, message.Params));
				break;

			case "MOTD":
				SendAll(await ircQueries.BuildMotdReplyAsync(User, cancellationToken));
				break;

			case "VERSION":
				Send(ircQueries.BuildVersionReply(User));
				break;

			case "TIME":
				Send(ircQueries.BuildTimeReply(User));
				break;

			case "LUSERS":
				SendAll(ircQueries.BuildLusersReply(User));
				break;

			case "AWAY":
				User.AwayMessage = first;
				break;

			case "PING":
				Send(IrcMessageWriter.Pong(first ?? ""));
				break;

			case "NICK" or "PASS" or "USER":
				// "Can I use another username? No." (osu!Bancho IRC FAQ). Identity is fixed at login.
				Send(Numeric(IrcNumeric.ErrAlreadyRegistered, "Changing nickname is not supported"));
				break;

			case "QUIT":
				logger.LogDebug("IRC client sent explicit QUIT: UserId={UserId}", User.Id);
				return false;

			case "CAP":
				HandleCap(message);
				break;

			case "PONG" or "AUTHENTICATE":
				// The client's own PONG closes the keepalive round trip, and SASL is never offered;
				// answering either with a numeric would break a flow the client considers mandatory.
				break;

			default:
				Send(Numeric(IrcNumeric.ErrUnknownCommand, message.Command, "Unknown command"));
				break;
		}

		return true;
	}

	/// <summary>
	///     Answers a capability negotiation. The gateway offers no capabilities, so a listing comes
	///     back empty, and a request is refused — a client that waits on either reply would otherwise
	///     stall before it ever registers.
	/// </summary>
	/// <remarks>
	///     Registration is never held pending negotiation: it completes as soon as PASS and NICK have
	///     both arrived, so an ending capability request lands afterward and needs no reply.
	/// </remarks>
	private void HandleCap(IrcMessage message)
	{
		var subcommand = message.Params.Count > 0 ? message.Params[0].ToUpperInvariant() : "";

		switch (subcommand)
		{
			case "LS" or "LIST":
				Send(new IrcMessage(options.Value.Name, "CAP", ["*", subcommand, ""]));
				break;
			case "REQ":
				Send(new IrcMessage(options.Value.Name, "CAP",
					["*", "NAK", message.Params.Count > 1 ? message.Params[1] : ""]));
				break;
			// END, ACK, and anything else need no reply.
		}
	}

	/// <summary>
	///     Reports whether chat may be sent to <paramref name="target" />, sending the numeric that
	///     explains the refusal when it may not. Only a channel target is checked; chat addressed to a
	///     nick that resolves to nobody is dropped without a reply.
	/// </summary>
	private bool EnsureCanSendTo(string target)
	{
		if (!target.StartsWith('#')) return true;

		var channel = channelRegistry.GetByName(target);
		if (channel is null)
		{
			Send(Numeric(IrcNumeric.ErrNoSuchChannel, target, "No such channel"));
			return false;
		}

		if (channel.Contains(User.Id) && channel.CanWrite(User.Privilege)) return true;

		Send(Numeric(IrcNumeric.ErrCannotSendToChannel, target, "Cannot send to channel"));
		return false;
	}

	private IrcMessage Numeric(IrcNumeric numeric, params string[] args)
	{
		return IrcMessageWriter.Numeric(options.Value.Name, numeric, User.Name, args);
	}

	private void SendAll(IEnumerable<IrcMessage> messages)
	{
		foreach (var message in messages) Send(message);
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