using System.Security.Cryptography;
using Basil.Application.Abstractions;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Protocol;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace Basil.Application.UseCases.Authentication;

/// <summary>
///     Ported from app/api/domains/cho.py's handle_osu_login_request (spec §3.1 in
///     docs/csharp-migration-plan.md). Login has no specific packet — it's the request the osu!
///     client sends without an "osu-token" header.
/// </summary>
public sealed class OsuLoginUseCase(
    IUserRepository users,
    IStatsRepository stats,
    IClientHashRepository clientHashes,
    IIngameLoginRepository ingameLogins,
    IChannelRegistry channelRegistry,
    IPlayerSessionRegistry sessionRegistry,
    IMailRepository mail,
    IRelationshipRepository relationships,
    IPasswordHasher passwordHasher,
    ILeaderboardStore leaderboardStore,
    ITokenGenerator tokenGenerator,
    IClock clock,
    IOptions<ServerBehaviorOptions> serverOptions)
{
    private const int FirstUserId = 3; // userid 2 is reserved for peppy/ppy, per bancho.py's base.sql auto_increment=3

    private static readonly string InactionableDiskSignatureMd5 =
        Convert.ToHexStringLower(MD5.HashData("0"u8.ToArray()));

    public async Task<OsuLoginResult> ExecuteAsync(OsuLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var loginData = LoginDataParser.Parse(request.Body);

        var osuVersion = OsuVersionParser.Parse(loginData.OsuVersion);
        if (osuVersion is null) return InvalidRequestFailure();

        IReadOnlyList<string> adapters;
        bool runningUnderWine;
        try
        {
            (adapters, runningUnderWine) = AdaptersStringParser.Parse(loginData.AdaptersString);
        }
        catch (FormatException)
        {
            return InvalidRequestFailure("invalid-adapters");
        }

        if (!(runningUnderWine || adapters.Any(a => a.Length > 0))) return InvalidRequestFailure("empty-adapters");

        var loginTime = clock.UtcNow.ToUnixTimeSeconds();

        // disallow multiple sessions from a single user, except tourney spectator clients.
        var existingSession = sessionRegistry.GetByName(loginData.Username);
        if (existingSession is not null && osuVersion.Stream != OsuStream.Tourney)
        {
            if (loginTime - existingSession.LastRecvTime < 10)
                return new OsuLoginResult("user-already-logged-in", Concat(
                    ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
                    ServerPacketWriter.Notification("User already logged in.")));

            sessionRegistry.Remove(existingSession);
        }

        var user = await users.FetchByNameAsync(loginData.Username, cancellationToken);
        if (user is null) return IncorrectCredentials();

        var passwordHash = await users.FetchPasswordHashAsync(user.Id, cancellationToken);
        if (passwordHash is null || !passwordHasher.Verify(loginData.PasswordMd5, passwordHash))
            return IncorrectCredentials();

        if (osuVersion.Stream == OsuStream.Tourney
            && !HasPriv(user.Priv, Privileges.Donator, Privileges.Unrestricted))
            return new OsuLoginResult("no",
                ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed));

        /* login credentials verified */

        await ingameLogins.CreateAsync(user.Id, request.Ip.ToString(), osuVersion.Date, StreamName(osuVersion.Stream),
            cancellationToken);

        await clientHashes.CreateAsync(
            user.Id, loginData.OsuPathMd5, loginData.AdaptersMd5, loginData.UninstallMd5, loginData.DiskSignatureMd5,
            cancellationToken);

        var diskSignatureForBanCheck = loginData.DiskSignatureMd5 == InactionableDiskSignatureMd5
            ? null
            : loginData.DiskSignatureMd5;

        var hardwareMatches = await clientHashes.FetchAnyHardwareMatchesForUserAsync(
            user.Id, runningUnderWine, loginData.AdaptersMd5, loginData.UninstallMd5, diskSignatureForBanCheck,
            cancellationToken);

        if (hardwareMatches.Count > 0
            && (user.Priv & (int)Privileges.Verified) == 0
            && hardwareMatches.Any(m => (m.Priv & (int)Privileges.Unrestricted) == 0))
            return new OsuLoginResult("contact-staff", Concat(
                ServerPacketWriter.Notification("Please contact staff directly to create an account."),
                ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed)));

        /* all checks passed, player is safe to login */

        var geoloc = GeolocationHeaderParser.TryParse(request.Headers) ?? GeolocationFromCountry(user.Country);

        if (user.Country == "xx") await users.UpdateCountryAsync(user.Id, geoloc.CountryAcronym, cancellationToken);

        var session = new PlayerSession(user.Id, user.Name, tokenGenerator.GenerateToken(), (Privileges)user.Priv,
            loginTime)
        {
            UtcOffset = loginData.UtcOffset,
            PmPrivate = loginData.PmPrivate,
            SilenceEnd = user.SilenceEnd,
            DonorEnd = user.DonorEnd,
            Client = new ClientDetails(
                osuVersion.Date, loginData.OsuPathMd5, loginData.AdaptersMd5, loginData.UninstallMd5,
                loginData.DiskSignatureMd5, adapters)
        };

        var data = new List<byte[]>
        {
            ServerPacketWriter.ProtocolVersion(19),
            ServerPacketWriter.LoginReply(session.Id),
            ServerPacketWriter.BanchoPrivileges((int)(session.BanchoPriv | ClientPrivileges.Supporter)),
            WelcomeNotification()
        };

        // send auto-join channel info; the client will attempt to join them.
        foreach (var channel in channelRegistry.AutoJoinChannels)
        {
            if (!channel.CanRead((Privileges)user.Priv) || channel.Name == "#lobby") continue;

            data.Add(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));

            foreach (var other in sessionRegistry.All)
                if (channel.CanRead(other.Priv))
                    other.Enqueue(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));
        }

        data.Add(ServerPacketWriter.ChannelInfoEnd());

        session.Geoloc = geoloc;

        // cache stats+rank for all 8 modes in memory (Player.stats_from_sql_full) — later packet
        // handlers (REQUEST_STATUS_UPDATE, USER_STATS_REQUEST, CHANGE_ACTION broadcast) read this
        // cache instead of re-querying the DB/Redis per packet.
        foreach (var modeStat in await stats.FetchAllForUserAsync(user.Id, cancellationToken))
        {
            var mode = (GameMode)modeStat.Mode;
            var modeRank = await leaderboardStore.FetchGlobalRankAsync(user.Id, mode, cancellationToken) ?? 0;
            session.ModeStats[mode] = new CachedPlayerStats(
                modeStat.Tscore, modeStat.Rscore, modeStat.Acc, modeStat.Plays, modeStat.Playtime,
                modeStat.MaxCombo, modeStat.TotalHits, modeRank);
        }

        var userRelationships = await relationships.FetchAllAsync(user.Id, null, cancellationToken);
        var friendIds = userRelationships.Where(r => r.Type == RelationshipType.Friend).Select(r => r.User2).ToList();

        data.Add(ServerPacketWriter.MainMenuIcon(serverOptions.Value.MenuIconUrl, serverOptions.Value.MenuOnclickUrl));
        data.Add(ServerPacketWriter.FriendsList(friendIds));
        data.Add(ServerPacketWriter.SilenceEnd((int)session.RemainingSilence));

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
                    // own in-memory cache (populated at its own login), no DB/Redis query needed.
                    data.Add(PacketBuilders.BuildUserPresence(other));
                    data.Add(PacketBuilders.BuildUserStats(other));
                }
            }

            var unreadMail = await mail.FetchUnreadMailToUserAsync(user.Id, cancellationToken);
            var sentTo = new HashSet<int>();
            foreach (var msg in unreadMail)
            {
                if (sentTo.Add(msg.FromId))
                    data.Add(ServerPacketWriter.SendMessage(msg.FromName, "Unread messages", msg.ToName, msg.FromId));

                var msgTime = msg.Time.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(msg.Time.Value).UtcDateTime
                    : DateTime.UtcNow;
                data.Add(ServerPacketWriter.SendMessage(msg.FromName, $"[{msgTime:ddd MMM d @ h:mmtt}] {msg.Msg}",
                    msg.ToName, msg.FromId));
            }

            if ((user.Priv & (int)Privileges.Verified) == 0)
            {
                var newPriv = (Privileges)user.Priv | Privileges.Verified;
                if (user.Id == FirstUserId)
                    newPriv |= Privileges.Staff | Privileges.Nominator | Privileges.Whitelisted
                               | Privileges.TourneyManager | Privileges.Donator | Privileges.Alumni;

                await users.UpdatePrivilegesAsync(user.Id, (int)newPriv, cancellationToken);
                session.Priv = newPriv;
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

        return new OsuLoginResult(session.Token, Concat([.. data]));
    }

    private static string StreamName(OsuStream stream)
    {
        return stream switch
        {
            OsuStream.Stable => "stable",
            OsuStream.Beta => "beta",
            OsuStream.CuttingEdge => "cuttingedge",
            OsuStream.Tourney => "tourney",
            OsuStream.Dev => "dev",
            _ => throw new ArgumentOutOfRangeException(nameof(stream), stream, null)
        };
    }

    private static bool HasPriv(int priv, Privileges required1, Privileges required2)
    {
        return (priv & (int)required1) != 0 && (priv & (int)required2) != 0;
    }

    // No network geolocation lookup — this server runs fully offline, so the fallback (when no
    // Cloudflare/nginx headers are present) is the country already stored at registration, with
    // lat/long left at 0.0 (matching Geolocation's own unresolved default).
    private static Geolocation GeolocationFromCountry(string country)
    {
        var acronym = country.ToLowerInvariant();
        var numeric = CountryCodes.ByAcronym.GetValueOrDefault(acronym, CountryCodes.ByAcronym["xx"]);
        return new Geolocation(0.0, 0.0, acronym, numeric);
    }

    private byte[] WelcomeNotification()
    {
        return ServerPacketWriter.Notification($"Welcome back to {serverOptions.Value.Domain}!\nRunning Basil.");
    }

    private static OsuLoginResult InvalidRequestFailure(string tokenOverride = "invalid-request")
    {
        return new OsuLoginResult(tokenOverride, Concat(
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
            ServerPacketWriter.Notification("Please restart your osu! and try again.")));
    }

    private OsuLoginResult IncorrectCredentials()
    {
        return new OsuLoginResult("incorrect-credentials", Concat(
            ServerPacketWriter.Notification($"{serverOptions.Value.Domain}: Incorrect credentials"),
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed)));
    }

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(p => p).ToArray();
    }
}