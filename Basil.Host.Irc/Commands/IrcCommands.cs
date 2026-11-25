using Basil.Application.Shared.Replies;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using System.Text;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Sessions;
using Basil.Gateway.Admission;
using Basil.Gateway.RateLimiting;
using Basil.Host.Irc.Clients;
using Basil.Host.Irc.Support;
using Basil.Host.Irc.Transport;
using Basil.Host.Shared.Info;
using Basil.Protocol.Irc;
using Basil.Protocol.Irc.Models;
using Basil.Shared.Structures.Cryptography;
using Mediator;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Commands;

[AdmissionPolicy]
internal sealed partial class IrcCommands(
	SessionRegistry registry,
	Server server,
	GeneralChannelRegistry general,
	Lobby lobby,
	RoomChannels roomChannels,
	ISender sender,
	Admission admission,
	SignInGate signIns,
	IrcClients clients,
	IrcPresence presence,
	IrcState state,
	IReplies<IrcReply> replies,
	IOptions<IrcOptions> options)
{
	private const int NickLength = 15;

	internal static readonly string[] Capabilities =
	[
		"cap-notify", "sasl", "server-time", "message-tags", "account-tag", "account-notify", "away-notify",
		"extended-join", "multi-prefix", "userhost-in-names", "echo-message", "standard-replies"
	];

	internal static IReadOnlyList<(string Name, IReadOnlyList<object> Metadata)> EntryPoints { get; } =
	[
		.. IrcReference.Commands.Select(command => (command.Command,
			(IReadOnlyList<object>)typeof(IrcCommands).GetCustomAttributes(true)))
	];

	private IrcOptions Options => options.Value;

	public async Task HandleAsync(IrcSocket session, IrcMessage message, CancellationToken ct)
	{
		if (session.Session is { } connection)
		{
			await sender.Send(new MarkSessionActive(connection), ct);
			// A closed session, such as one silent past its expiry, takes its socket with it.
			if (!connection.IsOpen)
			{
				session.Close(replies.Text(IrcReplies.Closing(connection.State.Record.Value.Ended?.Reason ?? SessionEndReason.TimedOut)));
				return;
			}
		}

		switch (message.Command)
		{
			case "CAP":
				await CapAsync(session, message, ct);
				return;
			case "PING":
				Ping(session, message);
				return;
			case "PONG":
				return;
			case IrcVerb.Quit:
				session.Quit = true;
				session.Close(message.Parameters.Count > 0 && message.Parameters[0].Length > 0
					? $"{replies.Text(IrcReply.ClosingQuit)}: {message.Parameters[0]}"
					: replies.Text(IrcReply.ClosingQuit));
				return;
		}

		if (session.Registered) await RegisteredAsync(session, message, ct);
		else await RegisteringAsync(session, message, ct);
	}

	private void Ping(IrcSocket session, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
			session.Numeric(IrcNumericReply.ErrNeedMoreParams, IrcVerb.Ping, replies.Text(IrcReply.NeedMoreParams));
		else
			session.Send(IrcMessage.ServerPong(session.ServerName, message.Parameters[0]));
	}

	private async Task RegisteringAsync(IrcSocket session, IrcMessage message, CancellationToken ct)
	{
		var parameters = message.Parameters;
		switch (message.Command)
		{
			case "PASS":
				if (parameters.Count == 0) NeedMore(session, "PASS");
				else session.Pass = parameters[0];
				break;
			case "NICK":
				if (parameters.Count == 0) session.Numeric(IrcNumericReply.ErrNoNicknameGiven, replies.Text(IrcReply.NoNicknameGiven));
				else session.RequestedNick = parameters[0];
				break;
			case "USER":
				if (parameters.Count < 4) NeedMore(session, "USER");
				else session.UserGiven = true;
				break;
			case "AUTHENTICATE":
				await AuthenticateAsync(session, message, ct);
				return;
			default:
				session.Numeric(IrcNumericReply.ErrNotRegistered, replies.Text(IrcReply.NotRegistered));
				return;
		}

		await TryCompleteAsync(session, ct);
	}

	private async Task TryCompleteAsync(IrcSocket session, CancellationToken ct)
	{
		if (session.Registered || session.Negotiating || session.RequestedNick is not { } nick || !session.UserGiven)
			return;

		if (session.Session is null)
		{
			if (session.Pass is not { } password)
			{
				Fail(session, IrcNumericReply.ErrPasswdMismatch, replies.Text(IrcReply.BadCredentials));
				return;
			}

			if (await SignInAsync(session, password, nick, ct) is { } failure)
			{
				Fail(session, IrcNumericReply.ErrPasswdMismatch, failure);
				return;
			}
		}

		await WelcomeAsync(session, ct);
	}

	private async Task<string?> SignInAsync(IrcSocket session, string password, string claimed,
		CancellationToken ct)
	{
		using var attempt = signIns.Admit(session.Address, claimed).Attempt;
		if (attempt is null) return replies.Text(IrcReply.TooManyAttempts);

		var result = await sender.Send(new LoginSession(
			new LoginAttempt(claimed, new Md5Hash(Encoding.UTF8.GetBytes(password))), SessionType.Irc,
			session.Address, null, 0), ct);
		if (result.Session is IrcSession connection)
		{
			session.Session = connection;
			clients.Bind(connection, session);
			return null;
		}

		attempt.End(result);
		return result.Failure is LoginFailure.AlreadyOnline ? replies.Text(IrcReply.AlreadyOnline) : replies.Text(IrcReply.BadCredentials);
	}

	private static void Fail(IrcSocket session, IrcNumericReply numeric, string reason)
	{
		session.Numeric(numeric, reason);
		session.Close(reason);
	}

	private void NeedMore(IrcSocket session, string command)
	{
		session.Numeric(IrcNumericReply.ErrNeedMoreParams, command, replies.Text(IrcReply.NeedMoreParams));
	}

	private async Task WelcomeAsync(IrcSocket session, CancellationToken ct)
	{
		var connection = session.Session!;
		var nick = session.Nick;
		if (session.RequestedNick is { } asked && !string.Equals(asked, nick, StringComparison.Ordinal))
			session.Send(IrcMessage.NickChange(asked, nick));

		var serverName = Options.ServerName;
		var mask = IrcView.Mask(connection.Record, Options.Domain);
		var motd = (await sender.Send(new ReadSettings(), ct)).General.Motd;
		session.Registered = true;
		session.Numeric(IrcNumericReply.RplWelcome, replies.Text(IrcReply.Welcome, new { network = IrcReplies.Network, mask = mask.ToString() }));
		session.Numeric(IrcNumericReply.RplYourHost,
			replies.Text(IrcReply.YourHost, new { serverName, version = AppVersion.Current }));
		session.Numeric(IrcNumericReply.RplCreated, replies.Text(IrcReply.Created, new { startedAt = $"{state.StartedAt:u}" }));
		session.Numeric(IrcNumericReply.RplMyInfo, serverName, AppVersion.Current, "-", "-");
		session.Numeric(IrcNumericReply.RplIsupport,
			[.. IrcIsupport.Tokens(IrcReplies.Network, NickLength), replies.Text(IrcReply.Supported)]);
		if (!string.IsNullOrWhiteSpace(motd)) SendMotd(session, motd);
		session.Ready();
	}

	private void SendMotd(IrcSocket session, string? motd)
	{
		if (string.IsNullOrWhiteSpace(motd))
		{
			session.Numeric(IrcNumericReply.ErrNoMotd, replies.Text(IrcReply.NoMotd));
			return;
		}

		session.Numeric(IrcNumericReply.RplMotdStart, replies.Text(IrcReply.MotdStart, new { serverName = session.ServerName }));
		foreach (var line in IrcText.Lines(motd))
			session.Numeric(IrcNumericReply.RplMotd, replies.Text(IrcReply.MotdLine, new { line }));
		session.Numeric(IrcNumericReply.RplEndOfMotd, replies.Text(IrcReply.EndOfMotd));
	}

	private async Task RegisteredAsync(IrcSocket session, IrcMessage message, CancellationToken ct)
	{
		var connection = session.Session!;
		switch (message.Command)
		{
			case "PRIVMSG" or "NOTICE":
				await MessageAsync(session, connection, message, ct);
				break;
			case "JOIN":
				await JoinAsync(session, connection, message, ct);
				break;
			case "PART":
				await PartAsync(session, connection, message, ct);
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
				await AwayAsync(session, connection, message, ct);
				break;
			case "TOPIC":
				Topic(session, connection, message);
				break;
			case "MODE":
				Mode(session, connection, message);
				break;
			case "MOTD":
				SendMotd(session, (await sender.Send(new ReadSettings(), ct)).General.Motd);
				break;
			case "KICK" or "INVITE":
				Management(session, message);
				break;
			case "AUTHENTICATE" when session.SaslDone:
				session.Numeric(IrcNumericReply.ErrSaslAlready, replies.Text(IrcReply.SaslAlready));
				break;
			case "NICK" or "PASS" or "USER" or "AUTHENTICATE":
				session.Numeric(IrcNumericReply.ErrAlreadyRegistered, replies.Text(IrcReply.AlreadyRegistered));
				break;
			default:
				session.Numeric(IrcNumericReply.ErrUnknownCommand, message.Command, replies.Text(IrcReply.UnknownCommand));
				break;
		}
	}

	private async Task CapAsync(IrcSocket session, IrcMessage message, CancellationToken ct)
	{
		var who = session.Registered ? session.Nick : "*";

		void Reply(string subcommand, string caps)
		{
			session.Send(IrcMessage.Cap(session.ServerName, who, subcommand, caps));
		}

		if (message.Parameters.Count == 0)
		{
			session.Numeric(IrcNumericReply.ErrNeedMoreParams, "CAP", replies.Text(IrcReply.NeedMoreParams));
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
				await TryCompleteAsync(session, ct);
				break;
			default:
				session.Numeric(IrcNumericReply.ErrUnknownCommand, "CAP", replies.Text(IrcReply.UnknownCommand));
				break;
		}
	}

	private static void CapLs(IrcSocket session, IrcMessage message, Action<string, string> reply)
	{
		if (!session.Registered) session.Negotiating = true;
		var version = 0;
		if (message.Parameters.Count > 1) int.TryParse(message.Parameters[1], out version);
		reply("LS", IrcCapabilities.Offer(Capabilities, version));
	}

	private static void CapReq(IrcSocket session, IrcMessage message, Action<string, string> reply)
	{
		if (!session.Registered) session.Negotiating = true;
		var requested = message.Parameters.Count > 1
			? message.Parameters[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
			: [];
		if (requested.All(cap => Capabilities.Contains(cap.TrimStart('-'))))
		{
			foreach (var cap in requested) session.Set(cap.TrimStart('-'), !cap.StartsWith('-'));
			reply("ACK", message.Parameters[1]);
		}
		else
		{
			reply("NAK", message.Parameters.Count > 1 ? message.Parameters[1] : string.Empty);
		}
	}

	private async Task AuthenticateAsync(IrcSocket session, IrcMessage message, CancellationToken ct)
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
			session.Numeric(IrcNumericReply.ErrSaslAborted, replies.Text(IrcReply.SaslAborted));
			return;
		}

		if (session.SaslDone)
		{
			session.Numeric(IrcNumericReply.ErrSaslAlready, replies.Text(IrcReply.SaslAlready));
			return;
		}

		if (!session.Has("sasl"))
		{
			session.Numeric(IrcNumericReply.ErrSaslFail, replies.Text(IrcReply.SaslFailed));
			return;
		}

		if (session.Sasl is null)
		{
			StartSasl(session, data);
			return;
		}

		if (data != "+") session.Sasl.Append(data);
		if (data.Length == IrcCapabilities.AuthenticateChunk) return;

		await FinishSaslAsync(session, ct);
	}

	private void StartSasl(IrcSocket session, string mechanism)
	{
		if (mechanism.Equals(IrcCapabilities.SaslMechanism, StringComparison.OrdinalIgnoreCase))
		{
			session.Sasl = new StringBuilder();
			session.Send(IrcMessage.Authenticate("+"));
		}
		else
		{
			session.Numeric(IrcNumericReply.RplSaslMechs, IrcCapabilities.SaslMechanism, replies.Text(IrcReply.SaslMechanisms));
			session.Numeric(IrcNumericReply.ErrSaslFail, replies.Text(IrcReply.SaslFailed));
		}
	}

	private async Task FinishSaslAsync(IrcSocket session, CancellationToken ct)
	{
		var payload = session.Sasl!.ToString();
		session.Sasl = null;
		if (!IrcCapabilities.TryDecodePlain(payload, out var account, out var key) ||
		    await SignInAsync(session, key, account, ct) is not null)
		{
			session.Numeric(IrcNumericReply.ErrSaslFail, replies.Text(IrcReply.SaslFailed));
			return;
		}

		var user = session.Session!.Record;
		session.Numeric(IrcNumericReply.RplLoggedIn, IrcView.Mask(user, Options.Domain).ToString(), user.Value.Name,
			replies.Text(IrcReply.LoggedInAs, new { name = user.Value.Name }));
		session.Numeric(IrcNumericReply.RplSaslSuccess, replies.Text(IrcReply.SaslSuccess));
		session.SaslDone = true;
		await TryCompleteAsync(session, ct);
	}

}