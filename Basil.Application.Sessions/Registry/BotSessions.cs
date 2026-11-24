using System.Net;
using Basil.Application.Sessions.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Client;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using SessionState = Basil.Domain.Sessions.Session;

namespace Basil.Application.Sessions.Registry;

/// <summary>Owns BasilBot's account and its server-owned sessions, and the operator session of the console.</summary>
public sealed class BotSessions
{
	private const string BotName = "BasilBot";

	private static readonly ClientBuild ServerClient =
		new(new OsuVersion(default, null, OsuVersionStream.Stable), default);

	private readonly SessionContext _context;
	private readonly SessionRegistry _registry;
	private readonly UserRegistry _users;

	internal BotSessions(SessionRegistry registry, SessionContext context, UserRegistry users)
	{
		_registry = registry;
		_context = context;
		_users = users;
	}

	/// <summary>Makes sure BasilBot's account exists with the privileges of a bot and no password.</summary>
	public async Task<UserRecord> EnsureBotAsync(CancellationToken ct = default)
	{
		var stored = await _context.Users.GetAsync(BasilBot.Id, ct);
		var isBot = stored is { Value: { DeletedAt: null, Privileges: UserPrivileges.Moderator, SilencedEnd: null } };

		if (!isBot && stored is not null) _registry.CloseAll(stored, SessionEndReason.Revoked);

		var bot = isBot
			? stored!
			: await _context.Users.ReplaceAsync(BasilBot.Id, stored is { Value.DeletedAt: null }
				? new UserData
					{ Name = stored.Value.Name, Country = stored.Value.Country, Privileges = UserPrivileges.Moderator }
				: new UserData { Name = BotName, Country = Country.Vn, Privileges = UserPrivileges.Moderator });

		await _context.Credentials.DeleteAsync(bot);
		return bot;
	}

	/// <summary>Creates the session the console acts through, with BasilBot's account.</summary>
	public async Task<OperatorSession> OperateAsync(CancellationToken ct = default)
	{
		var bot = await _context.Users.GetAsync(BasilBot.Id, ct) ??
		          await EnsureBotAsync(ct);
		var token = SessionRegistry.NewToken();
		var state = NewServerState(bot, SessionType.Api, token);
		return new OperatorSession(state, token)
			{ User = _users.Of(bot), Siblings = _registry.Of(bot), IsOpen = true };
	}

	/// <summary>Opens BasilBot's game client and IRC sessions where they are not open.</summary>
	/// <returns>The bot's sessions, or null when its account does not exist or a session could not open.</returns>
	public async Task<UserSessions?> ConnectBotAsync(CancellationToken ct = default)
	{
		var user = await _context.Users.GetAsync(BasilBot.Id, ct);
		if (user is null) return null;

		foreach (var type in (ReadOnlySpan<SessionType>)[SessionType.Stable, SessionType.Irc])
		{
			if (_registry.OnlineNow.GetValueOrDefault(user)?[type].Any(open => open.IsOpen) is true) continue;

			var token = SessionRegistry.NewToken();
			var session = _registry.NewSession(NewServerState(user, type, token), token, 0);
			if (_registry.Open(session) is not null) return null;
		}

		return _registry.OnlineNow.GetValueOrDefault(user);
	}

	private SessionState NewServerState(UserRecord bot, SessionType type, string token)
	{
		var now = _context.Clock.GetUtcNow();
		// A game client session must report a client, and none stands behind the bot's.
		var client = type is SessionType.Stable ? ServerClient : null;
		return SessionState.Start(SessionRegistry.NewSessionId(), bot, type,
			new SessionFingerprint(IPAddress.Loopback, client), SessionRegistry.HashToken(token), now);
	}
}
