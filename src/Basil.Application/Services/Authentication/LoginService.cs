using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Basil.Application.Abstractions.Login;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Formats;
using Basil.Application.Packets;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Content;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Login;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Basil.Protocol;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Authentication;

/// <summary>
///     Processes the osu! client's login request, validating credentials and client hardware against
///     the database and, on success, building the session and the full login packet bundle the
///     client expects.
/// </summary>
/// <remarks>
///     Login is not a packet exchange. The client sends a raw request with no <c>osu-token</c>
///     header, which <see cref="ExecuteAsync" /> decodes, validates, and answers with a
///     <see cref="LoginResult" /> holding the response body and, on success, the new session token.
///     A relogin evicts the user's previous session so a single account holds at most one online
///     session, except for tourney spectator clients.
/// </remarks>
public sealed class LoginService(
	IUserRepository users,
	IUserStatRepository userStatRepository,
	IClientHashRepository clientHashes,
	ILoginRepository loginRepository,
	IChannelRegistry channelRegistry,
	ISessionRegistry<GameSession> gameSessions,
	IRelationshipRepository relationships,
	IPasswordHasher passwordHasher,
	ITokenGenerator tokenGenerator,
	SpectatorService spectatorService,
	PlayerLogoutService playerLogoutService,
	IPlayerStatusEvents statusEvents,
	MenuIconService menuIconService,
	MotdService motdService,
	IOptions<ServerOptions> serverOptions,
	ILogger<LoginService> logger)
{
	public const int ReloginGuardWindowSeconds = 10;

	private static readonly string InactionableDiskSignatureMd5 =
		Convert.ToHexStringLower(MD5.HashData("0"u8.ToArray()));

	/// <summary>
	///     Executes the login handshake for a raw osu! client login request.
	/// </summary>
	/// <param name="request">The raw login request to process.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     A <see cref="LoginResult" /> carrying the packet body to send back to the client and the
	///     session token on success.
	/// </returns>
	public async Task<LoginResult> ExecuteAsync(LoginRequest request,
		CancellationToken cancellationToken = default)
	{
		LoginForm loginForm;
		try
		{
			loginForm = LoginForm.From(request.Body);
		}
		catch (ArgumentException)
		{
			return InvalidRequestFailure("invalid-request");
		}
		catch (FormatException)
		{
			return InvalidRequestFailure("invalid-adapters");
		}

		try
		{
			return await ExecuteAuthenticatedAsync(loginForm, request, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Every prior branch returns a LoginResult with a real cho-token, so the bancho route
			// handler can set the header unconditionally after awaiting this method — an exception
			// escaping here was the one path that skipped it (see docs/for-developers/bancho.md and
			// the 2026-08 investigation into missing cho-token headers under SQLite write
			// contention). ErrorOccurred is the protocol's own "internal error" login-failure code.
			logger.LogError(ex, "Login failed with an unexpected exception: Username={Username}",
				loginForm.Username);
			return new LoginResult("server-error", Concat(
				ServerPacketWriter.Notification("A server error occurred while logging in. Please try again."),
				ServerPacketWriter.LoginReply((int)LoginFailureReason.ErrorOccurred)));
		}
	}

	/// <summary>Runs the login checks and session setup once the raw request has parsed successfully.</summary>
	private async Task<LoginResult> ExecuteAuthenticatedAsync(LoginForm loginForm, LoginRequest request,
		CancellationToken cancellationToken)
	{
		var clientDetails = loginForm.ClientDetails;
		if (!(clientDetails.IsRunningUnderWine || clientDetails.Adapters.Any(a => a.Length > 0)))
			return InvalidRequestFailure("empty-adapters");

		var loginTime = DateTimeOffset.UtcNow;

		// disallow multiple game sessions from a single user, except tourney spectator clients. An
		// IrcSession for the same account, if any, is untouched — the two kinds coexist independently.
		var existingSession = gameSessions.GetByName(loginForm.Username);
		if (existingSession is not null && loginForm.OsuVersion.Stream != OsuStream.Tourney)
		{
			if (loginTime - existingSession.LastRecvTime < TimeSpan.FromSeconds(ReloginGuardWindowSeconds))
				return new LoginResult("user-already-logged-in", Concat(
					ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
					ServerPacketWriter.Notification("User already logged in.")));

			// Routes through the same teardown as a normal logout (match leave under lock,
			// spectator teardown including the bot's #spec_{userId} watch, channel parts, registry
			// removal, offline broadcast) instead of only removing the session from the registry.
			// The old bare Remove() left an evicted session's match slot permanently orphaned —
			// GhostDisconnectService only scans the registries it's still in, so a session already
			// evicted here could never be reaped — producing duplicate players after a taskkill
			// reconnect, "match is locked" from a slot nobody can ever free, and !mp make appearing
			// to kick its own creator (a stale Match reference tripping the AlreadyInMatch
			// tolerance in MatchMembershipService.CreateAsync). See RC3 in the 2026 investigation.
			logger.LogDebug("Existing session evicted on relogin: UserId={UserId}", existingSession.Id);
			await playerLogoutService.LogoutAsync(existingSession, cancellationToken);
		}

		var user = await users.FetchByNameAsync(loginForm.Username, cancellationToken);
		if (user is null) return IncorrectCredentials(loginForm.Username, request.Ip);

		var passwordHash = await users.FetchPasswordHashAsync(user.Id, cancellationToken);
		if (passwordHash is null
		    || !passwordHasher.Verify(Encoding.UTF8.GetBytes(loginForm.PasswordMd5), passwordHash))
			return IncorrectCredentials(loginForm.Username, request.Ip);

		// A deleted account must never be able to log back in, regardless of a correct password
		// (Issue #4). Reported as ordinary incorrect credentials rather than a distinct reason, so an
		// unauthenticated caller can't use login to probe whether a given username was deleted.
		if (user.DeletedAt is not null)
		{
			logger.LogDebug("Login rejected: account deleted. UserId={UserId} Username={Username}",
				user.Id, user.Name);
			return IncorrectCredentials(loginForm.Username, request.Ip);
		}

		if (loginForm.OsuVersion.Stream == OsuStream.Tourney
		    && !HasPrivileges(user.Privilege, UserPrivileges.Donator, UserPrivileges.Unrestricted))
		{
			logger.LogDebug("Tourney client rejected: not donator/unrestricted. Username={Username}", user.Name);
			return new LoginResult("no",
				ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed));
		}

		/* login credentials verified */

		await loginRepository.CreateAsync(user.Id, request.Ip.ToString(), loginForm.OsuVersion.Date,
			loginForm.OsuVersion.Stream.ToString().ToLowerInvariant(),
			cancellationToken);

		await clientHashes.CreateAsync(
			user.Id, clientDetails.OsuPathMd5, clientDetails.AdaptersMd5, clientDetails.UninstallMd5,
			clientDetails.DiskSignatureMd5, cancellationToken);

		var diskSignatureForBanCheck = clientDetails.DiskSignatureMd5 != InactionableDiskSignatureMd5
			? clientDetails.DiskSignatureMd5
			: null;

		var hardwareMatches = await clientHashes.FetchAnyHardwareMatchesForUserAsync(
			user.Id, clientDetails.IsRunningUnderWine, clientDetails.AdaptersMd5, clientDetails.UninstallMd5,
			diskSignatureForBanCheck, cancellationToken);

		if (hardwareMatches.Count > 0
		    && (user.Privilege & UserPrivileges.Verified) == 0
		    && hardwareMatches.Any(m => (m.Privilege & UserPrivileges.Unrestricted) == 0))
		{
			logger.LogInformation(
				"Login blocked: hardware ban match, unverified account. UserId={UserId} Username={Username} " +
				"MatchedUserIds={MatchedUserIds}",
				user.Id, user.Name, hardwareMatches.Select(m => m.UserId));
			return new LoginResult("contact-staff", Concat(
				ServerPacketWriter.Notification("Please contact staff directly to create an account."),
				ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed)));
		}

		/* all checks passed, userSession is safe to login */

		var session = new GameSession(user.Id, user.Name, $"osu-{tokenGenerator.GenerateToken()}", user.Privilege,
			loginTime)
		{
			UtcOffset = loginForm.UtcOffset,
			PmPrivate = loginForm.PmPrivate,
			SilenceEnd = user.SilenceEnd,
			Client = clientDetails,
			OsuVersion = loginForm.OsuVersion,
			Country = user.Country
		};

		var data = new List<byte[]>
		{
			ServerPacketWriter.ProtocolVersion(19),
			ServerPacketWriter.LoginReply(session.Id),
			ServerPacketWriter.BanchoPrivileges((int)(session.BanchoPrivilege | ClientPrivileges.Supporter))
		};

		if (await WelcomeNotification(cancellationToken) is { } notification)
			data.Add(notification);

		// send auto-join channel info; the client will attempt to join them.
		foreach (var channel in channelRegistry.AutoJoinChannels)
		{
			if (!channel.CanRead(user.Privilege) || channel.Name == "#lobby") continue;

			// Built once and shared by reference: the packet's content is identical for every
			// recipient (channel name/topic/player-count, not who's reading it), and Enqueue never
			// mutates what it's given, so rebuilding it per recipient was pure allocation waste that
			// scaled with online session count on every single login.
			var channelInfo = ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount);
			data.Add(channelInfo);

			foreach (var other in gameSessions.All)
				if (channel.CanRead(other.Privilege))
					other.Enqueue(channelInfo);
		}

		data.Add(ServerPacketWriter.ChannelInfoEnd());

		// cache stats+rank for all 8 modes in memory (User.stats_from_sql_full) — later packet
		// handlers (REQUEST_STATUS_UPDATE, USER_STATS_REQUEST, CHANGE_ACTION broadcast) read this
		// cache instead of re-querying the DB per packet; ScoreSubmissionService updates it directly
		// on submission so a fresh login isn't needed to see a just-bumped total. Rank is the
		// userSession's own user id rather than a real leaderboard position — just a stable,
		// distinguishable per-userSession number for the client's userSession-list/profile display.
		foreach (var (_, mode, totalScore, rankedScore, plays) in
		         await userStatRepository.FetchAllForUserAsync(user.Id, cancellationToken))
			session.ModeStats[mode] = new CachedPlayerStats(totalScore, rankedScore, plays, user.Id);

		var userRelationships = await relationships.FetchAllAsync(user.Id, null, cancellationToken);
		var friendIds = userRelationships.Where(r => r.Type == RelationshipType.Friend).Select(r => r.User2).ToList();

		var menuIconPath = await menuIconService.GetPathAsync(cancellationToken);
		if (menuIconPath is not null)
		{
			var menuIconUrl = MenuIconService.IsExternalUrl(menuIconPath)
				? menuIconPath
				: $"https://api.{serverOptions.Value.Domain}/menuicon/icon";
			var onclickUrl = await menuIconService.ReadUrlAsync(cancellationToken) ??
			                 "https://github.com/thnhmai06/osuBasil";
			data.Add(ServerPacketWriter.MainMenuIcon(menuIconUrl, onclickUrl));
		}
		else
		{
			data.Add(ServerPacketWriter.MainMenuIcon(string.Empty, string.Empty));
		}

		data.Add(ServerPacketWriter.FriendsList(friendIds));
		data.Add(ServerPacketWriter.SilenceEnd((int)session.RemainingSilence.TotalSeconds));

		var userPresenceAndStats =
			Concat(PacketBuilders.BuildUserPresence(session), PacketBuilders.BuildUserStats(session));

		data.Add(userPresenceAndStats);

		if (!session.Restricted)
		{
			foreach (var other in gameSessions.All)
			{
				other.Enqueue(userPresenceAndStats);

				if (!other.Restricted)
				{
					// `other` is an already-online session — its presence/stats are read from its
					// own in-memory cache (populated at its own login)
					data.Add(PacketBuilders.BuildUserPresence(other));
					data.Add(PacketBuilders.BuildUserStats(other));
				}
			}

			if ((user.Privilege & UserPrivileges.Verified) == 0)
			{
				var newPrivilege = user.Privilege | UserPrivileges.Verified;
				await users.UpdatePrivilegesAsync(user.Id, newPrivilege, cancellationToken);
				session.Privilege = newPrivilege;
			}
		}
		else
		{
			foreach (var other in gameSessions.All.Where(o => !o.Restricted))
			{
				data.Add(PacketBuilders.BuildUserPresence(other));
				data.Add(PacketBuilders.BuildUserStats(other));
			}

			data.Add(ServerPacketWriter.AccountRestricted());
		}

		if (!gameSessions.TryAdd(session))
			return new LoginResult("user-already-logged-in", Concat(
				ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
				ServerPacketWriter.Notification("User already logged in.")));

		// BasilBot spectates every userSession from the moment they log in, so their input can be
		// exposed externally via the api. host's SSE /spec/{id} channel — the real osu! client only
		// sends SpectateFrames packets while it believes it has >=1 spectator.
		var bot = gameSessions.GetByUserId(BotBootstrapService.BotId);
		if (bot is not null) spectatorService.AddSpectator(session, bot);

		if (statusEvents.HasSubscribers)
			statusEvents.PublishStatus(session.Id,
				JsonSerializer.SerializeToUtf8Bytes(PlayerStatusView.Build(session), BasilJsonOptions.Instance));

		logger.LogInformation("+ User logged in: UserId={UserId} Username={Username} Ip={Ip} Country={Country}",
			session.Id, session.Name, request.Ip, session.Country);
		return new LoginResult(session.Token, Concat([.. data]));
	}

	/// <summary>Determines whether a privilege set carries both of two required flags.</summary>
	/// <param name="privileges">The privilege set to the test.</param>
	/// <param name="required1">The first required flag.</param>
	/// <param name="required2">The second required flag.</param>
	/// <returns>
	///     <see langword="true" /> when both flags are set; otherwise, <see langword="false" />.
	/// </returns>
	private static bool HasPrivileges(UserPrivileges privileges, UserPrivileges required1, UserPrivileges required2)
	{
		return (privileges & required1) != 0 && (privileges & required2) != 0;
	}

	/// <summary>
	///     Returns the configured MOTD text as a notification packet, or <see langword="null" />
	///     when none is configured.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>
	///     The notification packet to append to the login bundle, or <see langword="null" /> for none.
	/// </returns>
	private async Task<byte[]?> WelcomeNotification(CancellationToken cancellationToken)
	{
		var text = await motdService.GetTextAsync(cancellationToken);
		return !string.IsNullOrWhiteSpace(text) ? ServerPacketWriter.Notification(text) : null;
	}

	/// <summary>
	///     Builds the failure result for a login request whose body could not be parsed.
	/// </summary>
	/// <param name="tokenOverride">The error-code string to place in the result's token slot.</param>
	/// <returns>
	///     A <see cref="LoginResult" /> telling the client to restart, with a failed login reply.
	/// </returns>
	private LoginResult InvalidRequestFailure(string tokenOverride)
	{
		logger.LogDebug("Login request rejected: malformed body. Reason={Reason}", tokenOverride);
		return new LoginResult(tokenOverride, Concat(
			ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
			ServerPacketWriter.Notification("Please restart your osu! and try again.")));
	}

	/// <summary>
	///     Builds the failure result for a login request with an unknown user or a bad password.
	/// </summary>
	/// <param name="username">The username that failed to authenticate.</param>
	/// <param name="ip">The client's IP address, recorded in the failure log.</param>
	/// <returns>
	///     A <see cref="LoginResult" /> carrying the "incorrect-credentials" error and the failure
	///     packets to send.
	/// </returns>
	private LoginResult IncorrectCredentials(string username, IPAddress ip)
	{
		logger.LogInformation("Login failed: incorrect credentials. Username={Username} Ip={Ip}", username, ip);
		return new LoginResult("incorrect-credentials", Concat(
			ServerPacketWriter.Notification(
				"Incorrect credentials. Please contact to the staffs if you don't know or forget the username/password."),
			ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed)));
	}

	/// <summary>Concatenates the given packet byte arrays in order.</summary>
	/// <param name="parts">The packet byte arrays to join.</param>
	/// <returns>A single byte array containing all the input bytes in order.</returns>
	private static byte[] Concat(params byte[][] parts)
	{
		return [.. parts.SelectMany(p => p)];
	}
}

/// <summary>
///     Represents the raw data of an incoming osu! client login request before it is parsed.
/// </summary>
/// <remarks>
///     The <see cref="Body" /> is the request's raw payload, decoded by
///     <see cref="LoginForm.From" />; <see cref="Ip" /> identifies the connecting client.
/// </remarks>
public sealed record LoginRequest(byte[] Body, IPAddress Ip);

/// <summary>
///     Represents the outcome of a login attempt: the token to issue the client and the packet
///     response body to send back.
/// </summary>
/// <remarks>
///     On success <see cref="OsuToken" /> carries the new session token. On failure, it carries an
///     error-code string instead, such as "incorrect-credentials", and the body holds the matching
///     notification and failure packets.
/// </remarks>
public sealed record LoginResult(string OsuToken, byte[] ResponseBody);