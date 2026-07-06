using System.Net;
using System.Text;
using Bancho.Application.Abstractions;
using Bancho.Application.Configuration;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Authentication;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bancho.Application.Tests.UseCases.Authentication;

/// <summary>
/// Ported from app/api/domains/cho.py's handle_osu_login_request — the 14-step login flow
/// (spec §3.1 in docs/csharp-migration-plan.md). Each early-exit validation branch is tested in
/// isolation with mocked ports; the happy path is exercised separately, verifying assembled
/// packet stream structure against ServerPacketWriter (already verified byte-exact in Phase 1).
/// </summary>
public class OsuLoginUseCaseTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IStatsRepository _stats = Substitute.For<IStatsRepository>();
    private readonly IClientHashRepository _clientHashes = Substitute.For<IClientHashRepository>();
    private readonly IIngameLoginRepository _ingameLogins = Substitute.For<IIngameLoginRepository>();
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IMailRepository _mail = Substitute.For<IMailRepository>();
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IGeolocationProvider _geolocationProvider = Substitute.For<IGeolocationProvider>();
    private readonly ILeaderboardStore _leaderboardStore = Substitute.For<ILeaderboardStore>();
    private readonly IOsuVersionAllowlistProvider _versionAllowlist = Substitute.For<IOsuVersionAllowlistProvider>();
    private readonly ITokenGenerator _tokenGenerator = Substitute.For<ITokenGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private OsuLoginUseCase MakeUseCase(bool disallowOldClients = false) => new(
        _users, _stats, _clientHashes, _ingameLogins, _channelRegistry, _sessionRegistry,
        _mail, _relationships, _passwordHasher, _geolocationProvider, _leaderboardStore,
        _versionAllowlist, _tokenGenerator, _clock,
        Options.Create(new RegistrationOptions { DisallowOldClients = disallowOldClients }),
        Options.Create(new ServerBehaviorOptions
        {
            Domain = "test.local", CommandPrefix = "!", MenuIconUrl = "https://a/i.png", MenuOnclickUrl = "https://a",
        }),
        Options.Create(new DiscordOptions { AuditLogWebhookUrl = "", InviteUrl = "https://discord.gg/x" }));

    private static byte[] LoginBody(
        string username = "cmyui",
        string passwordMd5 = "5f4dcc3b5aa765d61d8327deb882cf99",
        string osuVersion = "b20231231",
        string adapters = "001122334455.",
        int utcOffset = 0,
        bool displayCity = false,
        bool pmPrivate = false)
    {
        var clientHashes = $"osupathmd500000000000000000000000:{adapters}:adaptersmd5000000000000000000000:uninstallmd50000000000000000000:disksig00000000000000000000000000:";
        return Encoding.UTF8.GetBytes(
            $"{username}\n{passwordMd5}\n{osuVersion}|{utcOffset}|{(displayCity ? 1 : 0)}|{clientHashes}|{(pmPrivate ? 1 : 0)}\n");
    }

    [Fact]
    public async Task MalformedVersionString_ReturnsInvalidRequest()
    {
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(osuVersion: "not-a-version"), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("invalid-request", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
            ServerPacketWriter.Notification("Please restart your osu! and try again."));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task DisallowOldClients_VersionNotInAllowlist_ReturnsClientTooOld()
    {
        _versionAllowlist.GetAllowedVersionsAsync(OsuStream.Stable, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<DateOnly>?)new HashSet<DateOnly> { new(2024, 1, 1) });
        var useCase = MakeUseCase(disallowOldClients: true);
        var request = new OsuLoginRequest(LoginBody(osuVersion: "b20200101"), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("client-too-old", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.VersionUpdate(),
            ServerPacketWriter.LoginReply((int)LoginFailureReason.OldClient));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task DisallowOldClients_AllowlistFetchFails_AllowsConnectionThrough()
    {
        // osu!api unreachable -> null allowlist -> bancho.py allows the client through rather
        // than blocking legitimate players due to an outage. Proven here by observing it does
        // NOT stop at "client-too-old" — it proceeds to the next validation (adapters) instead.
        _versionAllowlist.GetAllowedVersionsAsync(Arg.Any<OsuStream>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<DateOnly>?)null);
        var useCase = MakeUseCase(disallowOldClients: true);
        var request = new OsuLoginRequest(LoginBody(osuVersion: "b20200101", adapters: "no-trailing-dot"), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("invalid-adapters", result.OsuToken);
    }

    [Fact]
    public async Task MalformedAdaptersString_ReturnsInvalidAdapters()
    {
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(adapters: "no-trailing-dot"), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("invalid-adapters", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
            ServerPacketWriter.Notification("Please restart your osu! and try again."));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task EmptyAdaptersNotUnderWine_ReturnsEmptyAdapters()
    {
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(adapters: "."), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("empty-adapters", result.OsuToken);
    }

    [Fact]
    public async Task DuplicateActiveSession_NonTourney_ReturnsUserAlreadyLoggedIn()
    {
        var existing = new PlayerSession(1, "cmyui", "old-token", Privileges.Unrestricted, loginTime: 0.0)
        {
            LastRecvTime = 995.0,
        };
        _sessionRegistry.GetByName("cmyui").Returns(existing);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1000));
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("user-already-logged-in", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
            ServerPacketWriter.Notification("User already logged in."));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task DuplicateExpiredSession_LogsOutOldSession_AndProceeds()
    {
        var existing = new PlayerSession(1, "cmyui", "old-token", Privileges.Unrestricted, loginTime: 0.0)
        {
            LastRecvTime = 900.0,
        };
        _sessionRegistry.GetByName("cmyui").Returns(existing);
        _clock.UtcNow.Returns(DateTimeOffset.FromUnixTimeSeconds(1000));
        _users.FetchByNameAsync("cmyui").Returns((Application.Abstractions.User?)null);
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        _sessionRegistry.Received(1).Remove(existing);
        Assert.Equal("incorrect-credentials", result.OsuToken);
    }

    [Fact]
    public async Task UnknownUsername_ReturnsIncorrectCredentials()
    {
        _users.FetchByNameAsync("cmyui").Returns((Application.Abstractions.User?)null);
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("incorrect-credentials", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.Notification("test.local: Incorrect credentials"),
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task WrongPassword_ReturnsIncorrectCredentials()
    {
        var user = MakeUser(id: 10, priv: (int)Privileges.Unrestricted);
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(10).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(false);
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("incorrect-credentials", result.OsuToken);
    }

    [Fact]
    public async Task TourneyStream_InsufficientPrivileges_ReturnsNo()
    {
        var user = MakeUser(id: 10, priv: (int)Privileges.Unrestricted); // no Donator -> insufficient for tourney
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(10).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(osuVersion: "b20231231tourney"), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("no", result.OsuToken);
        Assert.Equal(ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed), result.ResponseBody);
    }

    [Fact]
    public async Task GeolocationFails_ReturnsLoginFailed()
    {
        var user = MakeUser(id: 10, priv: (int)Privileges.Unrestricted);
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(10).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);
        _geolocationProvider.FetchByIpAsync(Arg.Any<IPAddress>(), Arg.Any<CancellationToken>())
            .Returns((Geolocation?)null);
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("login-failed", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.Notification("test.local: Login failed. Please contact an admin."),
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task HardwareBan_UnverifiedUserWithRestrictedMatch_ReturnsContactStaff()
    {
        var user = MakeUser(id: 10, priv: (int)Privileges.Unrestricted); // no Verified
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(10).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);
        _clientHashes.FetchAnyHardwareMatchesForUserAsync(10, false, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new ClientHashWithPlayer(99, "p", "a", "u", "d", DateTime.UtcNow, 1, "banned-user", (int)Privileges.Verified)]);
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("contact-staff", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.Notification("Please contact staff directly to create an account."),
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task HardwareMatch_VerifiedUser_AllowsLoginThrough()
    {
        SetUpHappyPath(out var user, priv: (int)(Privileges.Unrestricted | Privileges.Verified));
        _clientHashes.FetchAnyHardwareMatchesForUserAsync(user.Id, false, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new ClientHashWithPlayer(99, "p", "a", "u", "d", DateTime.UtcNow, 1, "other-account", (int)Privileges.Unrestricted)]);
        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("generated-token", result.OsuToken);
    }

    [Fact]
    public async Task HappyPath_UnrestrictedFirstLogin_GrantsVerifiedAndRegistersSession()
    {
        SetUpHappyPath(out var user, priv: (int)Privileges.Unrestricted); // not Verified yet

        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("generated-token", result.OsuToken);

        var expectedHeader = Concat(
            ServerPacketWriter.ProtocolVersion(19),
            ServerPacketWriter.LoginReply(user.Id));
        Assert.Equal(expectedHeader, result.ResponseBody.Take(expectedHeader.Length).ToArray());

        // first login grants VERIFIED (user.Id=10 here, not FIRST_USER_ID=3, so no bonus staff privs)
        await _users.Received(1).UpdatePrivilegesAsync(user.Id, (int)(Privileges.Unrestricted | Privileges.Verified), Arg.Any<CancellationToken>());
        _sessionRegistry.Received(1).Add(Arg.Is<PlayerSession>(s => s.Id == user.Id && s.Token == "generated-token"));
    }

    [Fact]
    public async Task HappyPath_FirstUserId_GrantsFullStaffPrivileges()
    {
        SetUpHappyPath(out _, priv: (int)Privileges.Unrestricted, userId: 3);

        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        await useCase.ExecuteAsync(request);

        var expectedPriv = Privileges.Unrestricted | Privileges.Verified | Privileges.Staff | Privileges.Nominator
            | Privileges.Whitelisted | Privileges.TourneyManager | Privileges.Donator | Privileges.Alumni;
        await _users.Received(1).UpdatePrivilegesAsync(3, (int)expectedPriv, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HappyPath_AlreadyVerifiedUser_DoesNotUpdatePrivileges()
    {
        SetUpHappyPath(out var user, priv: (int)(Privileges.Unrestricted | Privileges.Verified));

        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        await useCase.ExecuteAsync(request);

        await _users.DidNotReceive().UpdatePrivilegesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HappyPath_RestrictedUser_ResponseContainsAccountRestrictedPacket()
    {
        SetUpHappyPath(out _, priv: (int)Privileges.Verified); // no Unrestricted -> restricted

        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        // account_restricted has no payload -> its full wire bytes are a fixed constant, safe to search for.
        var restrictedPacketBytes = ServerPacketWriter.AccountRestricted();
        Assert.Contains(
            Convert.ToHexString(restrictedPacketBytes),
            Convert.ToHexString(result.ResponseBody));
    }

    [Fact]
    public async Task HappyPath_CachesAllModeStatsAndGeolocOnSession()
    {
        SetUpHappyPath(out var user, priv: (int)(Privileges.Unrestricted | Privileges.Verified));
        _stats.FetchAllForUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new Stats(user.Id, 0, 100_000, 90_000, 50, 1000, 95.5, 300, 2000, 10, 1, 2, 3, 4, 5),
            new Stats(user.Id, 1, 200_000, 180_000, 80, 2000, 90.0, 250, 3000, 20, 0, 0, 0, 0, 0),
        ]);
        _leaderboardStore.FetchGlobalRankAsync(user.Id, 0, Arg.Any<CancellationToken>()).Returns(7);

        PlayerSession? captured = null;
        _sessionRegistry.When(r => r.Add(Arg.Any<PlayerSession>())).Do(ci => captured = ci.Arg<PlayerSession>());

        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);
        await useCase.ExecuteAsync(request);

        Assert.NotNull(captured);
        Assert.Equal("us", captured!.Geoloc.CountryAcronym);
        Assert.Equal(2, captured.ModeStats.Count);
        Assert.Equal(7, captured.ModeStats[0].Rank);
        Assert.Equal(90_000, captured.ModeStats[0].Rscore);
    }

    [Fact]
    public async Task DbCountryXx_TriggersCountryUpdate()
    {
        SetUpHappyPath(out var user, priv: (int)(Privileges.Unrestricted | Privileges.Verified), country: "xx");

        var useCase = MakeUseCase();
        var request = new OsuLoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        await useCase.ExecuteAsync(request);

        await _users.Received(1).UpdateCountryAsync(user.Id, "us", Arg.Any<CancellationToken>());
    }

    private void SetUpHappyPath(out Application.Abstractions.User user, int priv, int userId = 10, string country = "us")
    {
        user = MakeUser(id: userId, priv: priv, country: country);
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(userId).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);
        _clientHashes.FetchAnyHardwareMatchesForUserAsync(userId, false, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _geolocationProvider.FetchByIpAsync(Arg.Any<IPAddress>(), Arg.Any<CancellationToken>())
            .Returns(new Geolocation(37.7749, -122.4194, "us", 225));
        _channelRegistry.AutoJoinChannels.Returns([]);
        _sessionRegistry.All.Returns([]);
        _stats.FetchAllForUserAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _relationships.FetchAllAsync(userId, null, Arg.Any<CancellationToken>()).Returns([]);
        _leaderboardStore.FetchGlobalRankAsync(userId, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((int?)null);
        _mail.FetchUnreadMailToUserAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _tokenGenerator.GenerateToken().Returns("generated-token");
        _stats.FetchOneAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((Application.Abstractions.Stats?)null);
    }

    private static Application.Abstractions.User MakeUser(int id, int priv, string country = "us", int clanId = 0) => new(
        Id: id, Name: "cmyui", SafeName: "cmyui", Email: "cmyui@example.test", Priv: priv, Country: country,
        SilenceEnd: 0, DonorEnd: 0, CreationTime: 0, LatestActivity: 0, ClanId: clanId, ClanPriv: 0,
        PreferredMode: 0, PlayStyle: 0, CustomBadgeName: null, CustomBadgeIcon: null, UserpageContent: null, ApiKey: null);

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
