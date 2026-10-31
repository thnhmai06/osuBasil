using System.Text;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Capabilities;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc;

/// <summary>Runs the commands of IRC clients: registration first, then chat through the clients' scoped objects.</summary>
internal sealed partial class IrcCommands(
	UserRegistry registry,
	Server server,
	GeneralChannelRegistry general,
	Lobby lobby,
	RoomChannels roomChannels,
	PmChannels pmChannels,
	ISettingsRepository settings,
	RateLimiter limiter,
	LoginAttemptGuard attempts,
	IrcClients clients,
	IrcPresence presence,
	IrcState state,
	IOptions<IrcOptions> options)
{
	private static readonly string[] Offered =
	[
		"cap-notify", "sasl", "server-time", "message-tags", "account-tag", "account-notify", "away-notify",
		"extended-join", "multi-prefix", "userhost-in-names", "echo-message", "standard-replies"
	];

	private IrcOptions Options => options.Value;

	/// <summary>Handles one line from a client.</summary>
	/// <param name="session">The socket that sent it.</param>
	/// <param name="message">The parsed line.</param>
	/// <param name="cancellationToken">A token that cancels the handling.</param>
	public async Task HandleAsync(IrcSession session, IrcMessage message, CancellationToken cancellationToken)
	{
		if (session.Connection is { } connection) registry.MarkActive(connection);

		switch (message.Command)
		{
			case "CAP":
				await CapAsync(session, message, cancellationToken);
				return;
			case "PING":
				Ping(session, message);
				return;
			case "PONG":
				return;
			case "QUIT":
				session.Quit = true;
				session.Close($"Quit: {(message.Parameters.Count > 0 ? message.Parameters[0] : string.Empty)}".TrimEnd());
				return;
		}

		if (session.Registered) await RegisteredAsync(session, message, cancellationToken);
		else await RegisteringAsync(session, message, cancellationToken);
	}

	private void Ping(IrcSession session, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
			session.Numeric(IrcNumericReply.ErrNeedMoreParams, "PING", IrcReplies.NeedMoreParams);
		else
			session.Send(new IrcMessage("PONG", session.ServerName, session.ServerName, message.Parameters[0]) { ForceTrailing = true });
	}

	private async Task RegisteringAsync(IrcSession session, IrcMessage message, CancellationToken cancellationToken)
	{
		var parameters = message.Parameters;
		switch (message.Command)
		{
			case "PASS":
				if (parameters.Count == 0) NeedMore(session, "PASS");
				else session.Pass = parameters[0];
				break;
			case "NICK":
				if (parameters.Count == 0) session.Numeric(IrcNumericReply.ErrNoNicknameGiven, "No nickname given");
				else session.RequestedNick = parameters[0];
				break;
			case "USER":
				if (parameters.Count < 4) NeedMore(session, "USER");
				else session.UserGiven = true;
				break;
			case "AUTHENTICATE":
				await AuthenticateAsync(session, message, cancellationToken);
				return;
			default:
				session.Numeric(IrcNumericReply.ErrNotRegistered, IrcReplies.NotRegistered);
				return;
		}

		await TryCompleteAsync(session, cancellationToken);
	}

	private async Task TryCompleteAsync(IrcSession session, CancellationToken cancellationToken)
	{
		if (session.Registered || session.Negotiating || session.RequestedNick is not { } nick || !session.UserGiven)
			return;

		if (session.Connection is null)
		{
			if (session.Pass is not { } key)
			{
				Fail(session, IrcNumericReply.ErrPasswdMismatch, IrcReplies.BadCredentials);
				return;
			}

			if (await SignInAsync(session, key, nick, cancellationToken) is { } failure)
			{
				Fail(session, IrcNumericReply.ErrPasswdMismatch, failure);
				return;
			}
		}

		await WelcomeAsync(session, cancellationToken);
	}

	/// <summary>Opens the connection a key belongs to, if the key's owner is the claimed account.</summary>
	/// <returns><see langword="null" /> on success, otherwise the text to close the socket with.</returns>
	private async Task<string?> SignInAsync(IrcSession session, string key, string claimed,
		CancellationToken cancellationToken)
	{
		var rules = (await settings.GetAsync(cancellationToken)).RateLimits;
		var ipKey = $"irc-login-fail:{session.Address}";
		var pairKey = $"{ipKey}:{claimed.ToLowerInvariant()}";
		using var attempt = attempts.TryReserve(ipKey, pairKey, rules.Login.MaxAttempts, rules.Login.Window);
		if (attempt is null) return IrcReplies.TooManyAttempts;

		if (!clients.Reserve(key, session)) return IrcReplies.KeyInUse;

		var result = await registry.LoginWithTokenAsync(SessionType.Irc, key, session.Address,
			owner => IrcView.IsNick(owner, claimed), cancellationToken);
		if (result.Connection is IrcConnection connection)
		{
			session.Key = key;
			session.Connection = connection;
			return null;
		}

		clients.Release(key, session);
		// Only a key that matches no session counts against the sign-in limit.
		if (result.Failure is LoginFailure.InvalidToken) attempt.Keep();
		return IrcReplies.BadCredentials;
	}

	private static void Fail(IrcSession session, IrcNumericReply numeric, string reason)
	{
		session.Numeric(numeric, reason);
		session.Close(reason);
	}

	private static void NeedMore(IrcSession session, string command)
	{
		session.Numeric(IrcNumericReply.ErrNeedMoreParams, command, IrcReplies.NeedMoreParams);
	}

	private async Task WelcomeAsync(IrcSession session, CancellationToken cancellationToken)
	{
		var connection = session.Connection!;
		var nick = session.Nick;
		if (session.RequestedNick is { } asked && !string.Equals(asked, nick, StringComparison.Ordinal))
			session.Send(new IrcMessage("NICK", asked, nick));

		var server = Options.ServerName;
		var mask = IrcView.Mask(connection.Record, Options.Domain);
		var motd = (await settings.GetAsync(cancellationToken)).General.Motd;
		session.Registered = true;
		session.Numeric(IrcNumericReply.RplWelcome, $"Welcome to the {IrcReplies.Network} IRC network, {mask}");
		session.Numeric(IrcNumericReply.RplYourHost, $"Your host is {server}, running version {IrcReplies.Version}");
		session.Numeric(IrcNumericReply.RplCreated, $"This server was started {state.StartedAt:u}");
		session.Numeric(IrcNumericReply.RplMyInfo, server, IrcReplies.Version, "-", "-");
		session.Numeric(IrcNumericReply.RplIsupport, "CASEMAPPING=ascii", "CHANTYPES=#", "PREFIX=(ov)@+",
			"CHANMODES=,,,", $"NETWORK={IrcReplies.Network}", "NICKLEN=15", "MODES=1", "MAXTARGETS=1", "LINELEN=512",
			"are supported by this server");
		SendMotd(session, motd);
		session.Ready();
	}

	private static void SendMotd(IrcSession session, string? motd)
	{
		if (string.IsNullOrWhiteSpace(motd))
		{
			session.Numeric(IrcNumericReply.ErrNoMotd, "MOTD File is missing");
			return;
		}

		session.Numeric(IrcNumericReply.RplMotdStart, $"- {session.ServerName} Message of the day -");
		foreach (var line in IrcText.Lines(motd))
			session.Numeric(IrcNumericReply.RplMotd, $"- {line}");
		session.Numeric(IrcNumericReply.RplEndOfMotd, "End of /MOTD command.");
	}

	private async Task RegisteredAsync(IrcSession session, IrcMessage message, CancellationToken cancellationToken)
	{
		var connection = session.Connection!;
		switch (message.Command)
		{
			case "PRIVMSG" or "NOTICE":
				await MessageAsync(session, connection, message, cancellationToken);
				break;
			case "JOIN":
				Join(session, connection, message);
				break;
			case "PART":
				Part(session, connection, message);
				break;
			case "NAMES":
				Names(session, connection, message);
				break;
			case "LIST":
				List(session, connection, message);
				break;
			case "WHO":
				Who(session, connection, message);
				break;
			case "WHOIS":
				Whois(session, connection, message);
				break;
			case "ISON":
				Ison(session, message);
				break;
			case "USERHOST":
				UserHost(session, message);
				break;
			case "AWAY":
				Away(session, connection, message);
				break;
			case "TOPIC":
				Topic(session, connection, message);
				break;
			case "MODE":
				Mode(session, connection, message);
				break;
			case "MOTD":
				SendMotd(session, (await settings.GetAsync(cancellationToken)).General.Motd);
				break;
			case "KICK" or "INVITE":
				Management(session, message);
				break;
			case "AUTHENTICATE" when session.SaslDone:
				session.Numeric(IrcNumericReply.ErrSaslAlready, "You have already authenticated");
				break;
			case "NICK" or "PASS" or "USER" or "AUTHENTICATE":
				session.Numeric(IrcNumericReply.ErrAlreadyRegistered, IrcReplies.AlreadyRegistered);
				break;
			default:
				session.Numeric(IrcNumericReply.ErrUnknownCommand, message.Command, IrcReplies.UnknownCommand);
				break;
		}
	}

	private async Task CapAsync(IrcSession session, IrcMessage message, CancellationToken cancellationToken)
	{
		var who = session.Registered ? session.Nick : "*";
		void Reply(string subcommand, string caps)
		{
			session.Send(new IrcMessage("CAP", session.ServerName, who, subcommand, caps) { ForceTrailing = true });
		}

		if (message.Parameters.Count == 0)
		{
			session.Numeric(IrcNumericReply.ErrNeedMoreParams, "CAP", IrcReplies.NeedMoreParams);
			return;
		}

		switch (message.Parameters[0].ToUpperInvariant())
		{
			case "LS":
				CapLs(session, message, Reply);
				break;
			case "LIST":
				Reply("LIST", string.Join(' ', session.Caps));
				break;
			case "REQ":
				CapReq(session, message, Reply);
				break;
			case "END":
				session.Negotiating = false;
				await TryCompleteAsync(session, cancellationToken);
				break;
			default:
				session.Numeric(IrcNumericReply.ErrUnknownCommand, "CAP", IrcReplies.UnknownCommand);
				break;
		}
	}

	private static void CapLs(IrcSession session, IrcMessage message, Action<string, string> reply)
	{
		if (!session.Registered) session.Negotiating = true;
		var v302 = message.Parameters.Count > 1 && int.TryParse(message.Parameters[1], out var version) && version >= 302;
		reply("LS", string.Join(' ', Offered.Select(cap => cap == "sasl" && v302 ? "sasl=PLAIN" : cap)));
	}

	private static void CapReq(IrcSession session, IrcMessage message, Action<string, string> reply)
	{
		if (!session.Registered) session.Negotiating = true;
		var requested = message.Parameters.Count > 1 ? message.Parameters[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
		if (requested.All(cap => Offered.Contains(cap.TrimStart('-'))))
		{
			foreach (var cap in requested) session.Set(cap.TrimStart('-'), !cap.StartsWith('-'));
			reply("ACK", message.Parameters[1]);
		}
		else
		{
			reply("NAK", message.Parameters.Count > 1 ? message.Parameters[1] : string.Empty);
		}
	}

	private async Task AuthenticateAsync(IrcSession session, IrcMessage message, CancellationToken cancellationToken)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, "AUTHENTICATE");
			return;
		}

		var data = message.Parameters[0];
		if (data == "*")
		{
			session.Sasl = null;
			session.Numeric(IrcNumericReply.ErrSaslAborted, "SASL authentication aborted");
			return;
		}

		if (session.SaslDone)
		{
			session.Numeric(IrcNumericReply.ErrSaslAlready, "You have already authenticated");
			return;
		}

		if (!session.Has("sasl"))
		{
			session.Numeric(IrcNumericReply.ErrSaslFail, "SASL authentication failed");
			return;
		}

		if (session.Sasl is null)
		{
			StartSasl(session, data);
			return;
		}

		if (data != "+") session.Sasl.Append(data);
		if (data.Length == 400) return;

		await FinishSaslAsync(session, cancellationToken);
	}

	private static void StartSasl(IrcSession session, string mechanism)
	{
		if (mechanism.Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
		{
			session.Sasl = new StringBuilder();
			session.Send(new IrcMessage("AUTHENTICATE", null, "+"));
		}
		else
		{
			session.Numeric(IrcNumericReply.RplSaslMechs, "PLAIN", "are available SASL mechanisms");
			session.Numeric(IrcNumericReply.ErrSaslFail, "SASL authentication failed");
		}
	}

	private async Task FinishSaslAsync(IrcSession session, CancellationToken cancellationToken)
	{
		var payload = session.Sasl!.ToString();
		session.Sasl = null;
		if (!TryDecodePlain(payload, out var account, out var key) ||
		    await SignInAsync(session, key, account, cancellationToken) is not null)
		{
			session.Numeric(IrcNumericReply.ErrSaslFail, "SASL authentication failed");
			return;
		}

		var user = session.Connection!.Record;
		session.Numeric(IrcNumericReply.RplLoggedIn, IrcView.Mask(user, Options.Domain).ToString(), user.Value.Name,
			$"You are now logged in as {user.Value.Name}");
		session.Numeric(IrcNumericReply.RplSaslSuccess, "SASL authentication successful");
		session.SaslDone = true;
		await TryCompleteAsync(session, cancellationToken);
	}

	private static bool TryDecodePlain(string payload, out string account, out string key)
	{
		account = key = string.Empty;
		try
		{
			var parts = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\0');
			if (parts.Length != 3) return false;
			(account, key) = (parts[1], parts[2]);
			return account.Length > 0 && key.Length > 0;
		}
		catch (FormatException)
		{
			return false;
		}
	}
}
