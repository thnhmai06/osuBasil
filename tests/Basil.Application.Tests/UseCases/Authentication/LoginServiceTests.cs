using System.Net;
using System.Text;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Services.Authentication;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Protocol;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Authentication;

/// <summary>
///     Ported from app/api/domains/cho.py's handle_osu_login_request — the 14-step login flow.
///     Each early-exit validation branch is tested in isolation with mocked ports; the happy path
///     is exercised separately, verifying the assembled packet stream structure against ServerPacketWriter.
/// </summary>
public class LoginServiceTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IClientHashRepository _clientHashes = Substitute.For<IClientHashRepository>();
    private readonly IIngameLoginRepository _ingameLogins = Substitute.For<IIngameLoginRepository>();
    private readonly ILeaderboardStore _leaderboardStore = Substitute.For<ILeaderboardStore>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IStatsRepository _stats = Substitute.For<IStatsRepository>();
    private readonly ITokenGenerator _tokenGenerator = Substitute.For<ITokenGenerator>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private LoginService MakeUseCase()
    {
        return new LoginService(
            _users, _stats, _clientHashes, _ingameLogins, _channelRegistry, _sessionRegistry,
            _relationships, _passwordHasher, _leaderboardStore,
            _tokenGenerator,
            Options.Create(new ServerOptions
            {
                Domain = "test.local", MenuIconPath = "icon.png",
                MenuOnclickUrl = "https://a"
            }));
    }

    private static byte[] LoginBody(
        string username = "cmyui",
        string passwordMd5 = "5f4dcc3b5aa765d61d8327deb882cf99",
        string osuVersion = "b20231231",
        string adapters = "001122334455.",
        int utcOffset = 0,
        bool displayCity = false,
        bool pmPrivate = false)
    {
        var clientHashes =
            $"osupathmd500000000000000000000000:{adapters}:adaptersmd5000000000000000000000:uninstallmd50000000000000000000:disksig00000000000000000000000000:";
        return Encoding.UTF8.GetBytes(
            $"{username}\n{passwordMd5}\n{osuVersion}|{utcOffset}|{(displayCity ? 1 : 0)}|{clientHashes}|{(pmPrivate ? 1 : 0)}\n");
    }

    [Fact]
    public async Task MalformedVersionString_ReturnsInvalidRequest()
    {
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(osuVersion: "not-a-version"), new Dictionary<string, string>(),
            IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("invalid-request", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed),
            ServerPacketWriter.Notification("Please restart your osu! and try again."));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task MalformedAdaptersString_ReturnsInvalidAdapters()
    {
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(adapters: "no-trailing-dot"), new Dictionary<string, string>(),
            IPAddress.Loopback);

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
        var request =
            new LoginRequest(LoginBody(adapters: "."), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("empty-adapters", result.OsuToken);
    }

    [Fact]
    public async Task DuplicateActiveSession_NonTourney_ReturnsUserAlreadyLoggedIn()
    {
        var existing = new PlayerSession(1, "cmyui", "old-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
        {
            LastRecvTime = DateTimeOffset.UtcNow.AddSeconds(-5)
        };
        _sessionRegistry.GetByName("cmyui").Returns(existing);
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

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
        var existing = new PlayerSession(1, "cmyui", "old-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
        {
            LastRecvTime = DateTimeOffset.UtcNow.AddSeconds(-100)
        };
        _sessionRegistry.GetByName("cmyui").Returns(existing);
        _users.FetchByNameAsync("cmyui").Returns((User?)null);
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        _sessionRegistry.Received(1).Remove(existing);
        Assert.Equal("incorrect-credentials", result.OsuToken);
    }

    [Fact]
    public async Task UnknownUsername_ReturnsIncorrectCredentials()
    {
        _users.FetchByNameAsync("cmyui").Returns((User?)null);
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("incorrect-credentials", result.OsuToken);
        var expected = Concat(
            ServerPacketWriter.Notification(
                "Incorrect credentials. Please contact to the staffs if you don't know or forget the username/password."),
            ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed));
        Assert.Equal(expected, result.ResponseBody);
    }

    [Fact]
    public async Task WrongPassword_ReturnsIncorrectCredentials()
    {
        var user = MakeUser(10, UserPrivileges.Unrestricted);
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(10).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(false);
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("incorrect-credentials", result.OsuToken);
    }

    [Fact]
    public async Task TourneyStream_InsufficientPrivileges_ReturnsNo()
    {
        var user = MakeUser(10, UserPrivileges.Unrestricted); // no Donator -> insufficient for tourney
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(10).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(osuVersion: "b20231231tourney"), new Dictionary<string, string>(),
            IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("no", result.OsuToken);
        Assert.Equal(ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed), result.ResponseBody);
    }

    [Fact]
    public async Task NoGeolocationHeaders_FallsBackToStoredCountry()
    {
        // No CF-IPCountry/X-Country-Code headers and no network geolocation lookup (offline
        // server) — the session's geoloc comes from the user's already-stored country instead.
        SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified, country: "jp");

        PlayerSession? captured = null;
        _sessionRegistry.When(r => r.Add(Arg.Any<PlayerSession>())).Do(ci => captured = ci.Arg<PlayerSession>());

        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);
        await useCase.ExecuteAsync(request);

        Assert.NotNull(captured);
        Assert.Equal("jp", captured!.Geoloc.Country.ToAcronym());
        Assert.Equal(0.0, captured.Geoloc.Latitude);
        Assert.Equal(0.0, captured.Geoloc.Longitude);
    }

    [Fact]
    public async Task HardwareBan_UnverifiedUserWithRestrictedMatch_ReturnsContactStaff()
    {
        var user = MakeUser(10, UserPrivileges.Unrestricted); // no Verified
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(10).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);
        _clientHashes.FetchAnyHardwareMatchesForUserAsync(10, false, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([
                new ClientHashWithPlayer(99, "p", "a", "u", "d", DateTime.UtcNow, 1, "banned-user",
                    UserPrivileges.Verified)
            ]);
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

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
        SetUpHappyPath(out var user, UserPrivileges.Unrestricted | UserPrivileges.Verified);
        _clientHashes.FetchAnyHardwareMatchesForUserAsync(user.Id, false, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([
                new ClientHashWithPlayer(99, "p", "a", "u", "d", DateTime.UtcNow, 1, "other-account",
                    UserPrivileges.Unrestricted)
            ]);
        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("generated-token", result.OsuToken);
    }

    [Fact]
    public async Task HappyPath_UnrestrictedFirstLogin_GrantsVerifiedAndRegistersSession()
    {
        SetUpHappyPath(out var user, UserPrivileges.Unrestricted); // not Verified yet

        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        var result = await useCase.ExecuteAsync(request);

        Assert.Equal("generated-token", result.OsuToken);

        var expectedHeader = Concat(
            ServerPacketWriter.ProtocolVersion(19),
            ServerPacketWriter.LoginReply(user.Id));
        Assert.Equal(expectedHeader, result.ResponseBody.Take(expectedHeader.Length).ToArray());

        // first login grants VERIFIED (user.Id=10 here, not FIRST_USER_ID=3, so no bonus staff privs)
        await _users.Received(1).UpdatePrivilegesAsync(user.Id, UserPrivileges.Unrestricted | UserPrivileges.Verified,
            Arg.Any<CancellationToken>());
        _sessionRegistry.Received(1).Add(Arg.Is<PlayerSession>(s => s.Id == user.Id && s.Token == "generated-token"));
    }

    [Fact]
    public async Task HappyPath_AlreadyVerifiedUser_DoesNotUpdatePrivileges()
    {
        SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

        await useCase.ExecuteAsync(request);

        await _users.DidNotReceive()
            .UpdatePrivilegesAsync(Arg.Any<int>(), Arg.Any<UserPrivileges>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HappyPath_RestrictedUser_ResponseContainsAccountRestrictedPacket()
    {
        SetUpHappyPath(out _, UserPrivileges.Verified); // no Unrestricted -> restricted

        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);

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
        SetUpHappyPath(out var user, UserPrivileges.Unrestricted | UserPrivileges.Verified);
        _stats.FetchAllForUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new Stats(user.Id, GameMode.Standard, 100_000, 90_000, 50, 95.5),
            new Stats(user.Id, GameMode.Taiko, 200_000, 180_000, 80, 90.0)
        ]);
        _leaderboardStore.FetchGlobalRankAsync(user.Id, GameMode.Standard, Arg.Any<CancellationToken>()).Returns(7);

        PlayerSession? captured = null;
        _sessionRegistry.When(r => r.Add(Arg.Any<PlayerSession>())).Do(ci => captured = ci.Arg<PlayerSession>());

        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);
        await useCase.ExecuteAsync(request);

        Assert.NotNull(captured);
        Assert.Equal("us", captured!.Geoloc.Country.ToAcronym());
        Assert.Equal(2, captured.ModeStats.Count);
        Assert.Equal(7, captured.ModeStats[GameMode.Standard].Rank);
        Assert.Equal(90_000, captured.ModeStats[GameMode.Standard].Rscore);
    }

    [Fact]
    public async Task DbCountryXx_HeaderGeolocationDiffers_TriggersCountryUpdate()
    {
        // Only a header-supplied geoloc (Cloudflare/nginx) can differ from the stored country now
        // that there's no network geolocation fallback — the DB-fallback path (no headers) always
        // resolves to the stored country itself, so it can never trigger this update.
        SetUpHappyPath(out var user, UserPrivileges.Unrestricted | UserPrivileges.Verified, country: "xx");
        var headers = new Dictionary<string, string>
        {
            ["CF-IPCountry"] = "US", ["CF-IPLatitude"] = "37.7749", ["CF-IPLongitude"] = "-122.4194"
        };

        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), headers, IPAddress.Loopback);

        await useCase.ExecuteAsync(request);

        await _users.Received(1).UpdateCountryAsync(user.Id, "us", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MotdFile_WhenFileExists_SendsNotification()
    {
        var motdPath = Path.Combine(AppContext.BaseDirectory, "Data", "MOTD.txt");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(motdPath)!);
            await File.WriteAllTextAsync(motdPath, "Test message of the day!");
            SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

            var useCase = MakeUseCase();
            var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);
            var result = await useCase.ExecuteAsync(request);

            var notificationPacket = ServerPacketWriter.Notification("Test message of the day!");
            var bodyHex = Convert.ToHexString(result.ResponseBody);
            Assert.Contains(Convert.ToHexString(notificationPacket), bodyHex);
        }
        finally
        {
            File.Delete(motdPath);
        }
    }

    [Fact]
    public async Task MotdFile_WhenFileMissing_SendsNoNotification()
    {
        var motdPath = Path.Combine(AppContext.BaseDirectory, "Data", "MOTD.txt");
        if (File.Exists(motdPath)) File.Delete(motdPath);

        SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

        var useCase = MakeUseCase();
        var request = new LoginRequest(LoginBody(), new Dictionary<string, string>(), IPAddress.Loopback);
        var result = await useCase.ExecuteAsync(request);

        // No welcome notification: the response starts with ProtocolVersion + LoginReply directly.
        var expectedHeader = Concat(
            ServerPacketWriter.ProtocolVersion(19),
            ServerPacketWriter.LoginReply(10));
        Assert.Equal(expectedHeader, result.ResponseBody.Take(expectedHeader.Length).ToArray());
    }

    private void SetUpHappyPath(out User user, UserPrivileges priv, int userId = 10, string country = "us")
    {
        user = MakeUser(userId, priv, country);
        _users.FetchByNameAsync("cmyui").Returns(user);
        _users.FetchPasswordHashAsync(userId).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);
        _clientHashes.FetchAnyHardwareMatchesForUserAsync(userId, false, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _channelRegistry.AutoJoinChannels.Returns([]);
        _sessionRegistry.All.Returns([]);
        _stats.FetchAllForUserAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _relationships.FetchAllAsync(userId, null, Arg.Any<CancellationToken>()).Returns([]);
        _leaderboardStore.FetchGlobalRankAsync(userId, Arg.Any<GameMode>(), Arg.Any<CancellationToken>())
            .Returns((int?)null);
        _tokenGenerator.GenerateToken().Returns("generated-token");
    }

    private static User MakeUser(int id, UserPrivileges priv, string country = "us")
    {
        return new User(id, "cmyui", Enum.Parse<Country>(country, ignoreCase: true), priv, default);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(p => p).ToArray();
    }
}