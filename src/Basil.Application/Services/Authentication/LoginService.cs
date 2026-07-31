using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Content;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Protocol;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Authentication;

/// <summary>
///     Ported from app/api/domains/cho.py's handle_osu_login_request (spec §3.1 in
///     docs/csharp-migration-plan.md). Login has no specific packet — it's the request the osu!
///     Client sends it without an "osu-token" header.
/// </summary>
public sealed class LoginService(
	IUserRepository users,
	IStatsRepository stats,
	IClientHashRepository clientHashes,
	IIngameLoginRepository ingameLogins,
	IChannelRegistry channelRegistry,
	IPlayerSessionRegistry sessionRegistry,
	IRelationshipRepository relationships,
	IPasswordHasher passwordHasher,
	ILeaderboardStore leaderboardStore,
	ITokenGenerator tokenGenerator,
	SpectatorService spectatorService,
	MenuIconService menuIconService,
	IOptions<ServerOptions> serverOptions,
	ILogger<LoginService> logger)
{
	private static readonly string MotdPath = Path.Combine("Data", "MOTD.txt");

	private static readonly string InactionableDiskSignatureMd5 =
		Convert.ToHexStringLower(MD5.HashData("0"u8.ToArray()));

	public async Task<LoginResult> ExecuteAsync(LoginRequest request,
		CancellationToken cancellationToken = default)
	{
		LoginData loginData;
		try
		{
			loginData = LoginData.From(request.Body);
		}
		catch (ArgumentException)
		{
			return InvalidRequestFailure("invalid-request");
		}
		catch (FormatException)
		{
			return InvalidRequestFailure("invalid-adapters");
		}

		var clientDetails = loginData.ClientDetails;
		if (!(clientDetails.IsRunningUnderWine || clientDetails.Adapters.Any(a => a.Length > 0)))
			return InvalidRequestFailure("empty-adapters");

		var loginTime = DateTimeOffset.UtcNow;

		// disallow multiple sessions from a single user, except tourney spectator clients.
		var existingSession = sessionRegistry.GetByName(loginData.Username);
		if (existingSession is not null && loginData.OsuVersion.Stream != OsuStream.Tourney)
		{
			if (loginTime - existingSession.LastRecvTime < TimeSpan.FromSeconds(10))
				return new LoginResult("user-already-logged-in", Concat(
					ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
					ServerPacketWriter.Notification("User already logged in.")));

			// #spec_{userId} is keyed by the persistent user id, stable across relogins — tear down
			// the bot's spectate relationship on the departing session now, or a relogin would pile
			// a dead member reference onto the channel the new session's own AddSpectator call
			// below re-creates.
			var staleBot = sessionRegistry.GetById(BotBootstrapService.BotId);
			if (staleBot is not null) spectatorService.RemoveSpectator(existingSession, staleBot);

			logger.LogDebug("Existing session evicted on relogin: UserId={UserId}", existingSession.Id);
			sessionRegistry.Remove(existingSession);
		}

		var user = await users.FetchByNameAsync(loginData.Username, cancellationToken);
		if (user is null) return IncorrectCredentials(loginData.Username, request.Ip);

		var passwordHash = await users.FetchPasswordHashAsync(user.Id, cancellationToken);
		if (passwordHash is null
		    || !passwordHasher.Verify(Encoding.UTF8.GetBytes(loginData.PasswordMd5), passwordHash))
			return IncorrectCredentials(loginData.Username, request.Ip);

		if (loginData.OsuVersion.Stream == OsuStream.Tourney
		    && !HasPrivileges(user.Privilege, UserPrivileges.Donator, UserPrivileges.Unrestricted))
		{
			logger.LogDebug("Tourney client rejected: not donator/unrestricted. Username={Username}", user.Name);
			return new LoginResult("no",
				ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed));
		}

		/* login credentials verified */

		await ingameLogins.CreateAsync(user.Id, request.Ip.ToString(), loginData.OsuVersion.Date,
			loginData.OsuVersion.Stream.ToString().ToLowerInvariant(),
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

		/* all checks passed, player is safe to login */

		var geolocation = Geolocation.From(request.Headers) ?? GeolocationFromCountry(user.Country);

		if (user.Country == Country.Xx)
			await users.UpdateCountryAsync(user.Id, geolocation.Country, cancellationToken);

		var session = new PlayerSession(user.Id, user.Name, tokenGenerator.GenerateToken(), user.Privilege, loginTime)
		{
			UtcOffset = loginData.UtcOffset,
			PmPrivate = loginData.PmPrivate,
			SilenceEnd = user.SilenceEnd,
			Client = clientDetails,
			OsuVersion = loginData.OsuVersion
		};

		var data = new List<byte[]>
		{
			ServerPacketWriter.ProtocolVersion(19),
			ServerPacketWriter.LoginReply(session.Id),
			ServerPacketWriter.BanchoPrivileges((int)(session.BanchoPrivilege | ClientPrivileges.Supporter))
		};

		if (WelcomeNotification() is { } notification)
			data.Add(notification);

		// send auto-join channel info; the client will attempt to join them.
		foreach (var channel in channelRegistry.AutoJoinChannels)
		{
			if (!channel.CanRead(user.Privilege) || channel.Name == "#lobby") continue;

			data.Add(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));

			foreach (var other in sessionRegistry.All)
				if (channel.CanRead(other.Privilege))
					other.Enqueue(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));
		}

		data.Add(ServerPacketWriter.ChannelInfoEnd());

		session.Geoloc = geolocation;

		// cache stats+rank for all 8 modes in memory (Player.stats_from_sql_full) — later packet
		// handlers (REQUEST_STATUS_UPDATE, USER_STATS_REQUEST, CHANGE_ACTION broadcast) read this
		// cache instead of re-querying the DB/Redis per packet.
		foreach (var (_, mode, tscore, rscore, plays, acc) in
		         await stats.FetchAllForUserAsync(user.Id, cancellationToken))
		{
			var modeRank = await leaderboardStore.FetchGlobalRankAsync(user.Id, mode, cancellationToken) ?? 0;
			session.ModeStats[mode] = new CachedPlayerStats(
				tscore, rscore, acc, plays, modeRank);
		}

		var userRelationships = await relationships.FetchAllAsync(user.Id, null, cancellationToken);
		var friendIds = userRelationships.Where(r => r.Type == RelationshipType.Friend).Select(r => r.User2).ToList();

		if (menuIconService.FindIconPath() is not null)
		{
			var menuIconUrl = $"https://api.{serverOptions.Value.Domain}/menuicon/icon";
			var onclickUrl = menuIconService.ReadUrl() ?? "https://github.com/thnhmai06/osuBasil";
			data.Add(ServerPacketWriter.MainMenuIcon(menuIconUrl, onclickUrl));
		}

		data.Add(ServerPacketWriter.FriendsList(friendIds));
		data.Add(ServerPacketWriter.SilenceEnd((int)session.RemainingSilence.TotalSeconds));

		var userPresenceAndStats =
			Concat(PacketBuilders.BuildUserPresence(session), PacketBuilders.BuildUserStats(session));

		data.Add(userPresenceAndStats);

		if (!session.Restricted)
		{
			foreach (var other in sessionRegistry.All)
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
			foreach (var other in sessionRegistry.All.Where(o => !o.Restricted))
			{
				data.Add(PacketBuilders.BuildUserPresence(other));
				data.Add(PacketBuilders.BuildUserStats(other));
			}

			data.Add(ServerPacketWriter.AccountRestricted());
		}

		sessionRegistry.Add(session);

		// BasilBot spectates every player from the moment they log in, so their input can be
		// exposed externally via the api. host's SSE /spec/{id} channel — the real osu! client only
		// sends SpectateFrames packets while it believes it has >=1 spectator.
		var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
		if (bot is not null) spectatorService.AddSpectator(session, bot);

		logger.LogInformation("+ User logged in: UserId={UserId} Username={Username} Ip={Ip} Country={Country}",
			session.Id, session.Name, request.Ip, geolocation.Country);
		return new LoginResult(session.Token, Concat([.. data]));
	}

	private static bool HasPrivileges(UserPrivileges privileges, UserPrivileges required1, UserPrivileges required2)
	{
		return (privileges & required1) != 0 && (privileges & required2) != 0;
	}

	// No network geolocation lookup — this server runs fully offline, so the fallback (when no
	// Cloudflare/nginx headers are present) is the country already stored at registration, with
	// lat/long left at 0.0 (matching Geolocation's own unresolved default).
	private static Geolocation GeolocationFromCountry(Country country)
	{
		return new Geolocation(0.0, 0.0, country);
	}

	private static byte[]? WelcomeNotification()
	{
		var motdPath = Path.Combine(AppContext.BaseDirectory, MotdPath);
		if (!File.Exists(motdPath)) return null;
		var text = File.ReadAllText(motdPath).TrimEnd();
		return !string.IsNullOrEmpty(text) ? ServerPacketWriter.Notification(text) : null;
	}

	private LoginResult InvalidRequestFailure(string tokenOverride)
	{
		logger.LogDebug("Login request rejected: malformed body. Reason={Reason}", tokenOverride);
		return new LoginResult(tokenOverride, Concat(
			ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
			ServerPacketWriter.Notification("Please restart your osu! and try again.")));
	}

	private LoginResult IncorrectCredentials(string username, IPAddress ip)
	{
		logger.LogInformation("Login failed: incorrect credentials. Username={Username} Ip={Ip}", username, ip);
		return new LoginResult("incorrect-credentials", Concat(
			ServerPacketWriter.Notification(
				"Incorrect credentials. Please contact to the staffs if you don't know or forget the username/password."),
			ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed)));
	}

	private static byte[] Concat(params byte[][] parts)
	{
		return [.. parts.SelectMany(p => p)];
	}
}