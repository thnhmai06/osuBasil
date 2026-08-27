using System.Net;
using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Login;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Settings;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Authentication;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Content;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LoginRequest = Basil.Application.Services.Authentication.LoginRequest;

namespace Basil.Application.Tests.Services.Authentication;

/// <summary>
///     Verifies the login flow: each early-exit validation branch is tested in isolation with mocked
///     ports, and the happy path is exercised separately, verifying the assembled packet stream
///     structure against ServerPacketWriter.
/// </summary>
public class LoginServiceTests
{
	private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
	private readonly IClientHashRepository _clientHashes = Substitute.For<IClientHashRepository>();
	private readonly ILoginRepository _loginRepository = Substitute.For<ILoginRepository>();
	private readonly MenuIconService _menuIconService;
	private readonly MotdService _motdService;
	private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
	private readonly IRelationshipRepository _relationships = Substitute.For<IRelationshipRepository>();
	private readonly ISessionRegistry<GameSession> _sessionRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

	private readonly PlayerLogoutService _playerLogoutService;
	private readonly SpectatorService _spectatorService;
	private readonly ITokenGenerator _tokenGenerator = Substitute.For<ITokenGenerator>();
	private readonly IUserStatRepository _userStatRepository = Substitute.For<IUserStatRepository>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	public LoginServiceTests()
	{
		var ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
		var channelMembership = new ChannelMembershipService(_sessionRegistry, ircRegistry, _channelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		_spectatorService = new SpectatorService(_channelRegistry, channelMembership,
			NullLogger<SpectatorService>.Instance);
		var matchMembership = new MatchMembershipService(Substitute.For<IMatchRegistry>(), _channelRegistry,
			_sessionRegistry, ircRegistry, channelMembership, Substitute.For<IMatchRepository>(),
			Substitute.For<IMatchLiveEvents>(), Substitute.For<IBeatmapRepository>(), _users,
			NullLogger<MatchMembershipService>.Instance);
		_playerLogoutService = new PlayerLogoutService(_sessionRegistry, ircRegistry, channelMembership,
			_spectatorService, matchMembership, NullLogger<PlayerLogoutService>.Instance);
		_menuIconService = new MenuIconService(_settings);
		_motdService = new MotdService(_settings);
		// NSubstitute's default for an unconfigured string-returning member is "", not null — stub
		// this explicitly so every test's happy path matches "no MOTD configured" unless overridden.
		_settings.GetAsync("Motd", Arg.Any<CancellationToken>()).Returns((string?)null);
		// TryAdd's unconfigured NSubstitute default is false — stub it true so the happy
		// path doesn't spuriously hit the "user-already-logged-in" branch.
		_sessionRegistry.TryAdd(Arg.Any<GameSession>()).Returns(true);
	}

	private LoginService MakeUseCase()
	{
		return new LoginService(
			_users, _userStatRepository, _clientHashes, _loginRepository, _channelRegistry, _sessionRegistry,
			_relationships, _passwordHasher, _tokenGenerator, _spectatorService, _playerLogoutService,
			_menuIconService, _motdService,
			Options.Create(new ServerOptions
			{
				Domain = "test.local"
			}), NullLogger<LoginService>.Instance);
	}

	private static GameSession MakeBot()
	{
		return new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
			{ IsBot = true };
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
		var request = new LoginRequest(LoginBody(osuVersion: "not-a-version"),
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
		var request = new LoginRequest(LoginBody(adapters: "no-trailing-dot"),
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
			new LoginRequest(LoginBody(adapters: "."), IPAddress.Loopback);

		var result = await useCase.ExecuteAsync(request);

		Assert.Equal("empty-adapters", result.OsuToken);
	}

	[Fact]
	public async Task DuplicateActiveSession_NonTourney_ReturnsUserAlreadyLoggedIn()
	{
		var existing = new GameSession(1, "cmyui", "old-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			LastRecvTime = DateTimeOffset.UtcNow.AddSeconds(-5)
		};
		_sessionRegistry.GetByName("cmyui").Returns(existing);
		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

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
		var existing = new GameSession(1, "cmyui", "old-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			LastRecvTime = DateTimeOffset.UtcNow.AddSeconds(-100)
		};
		_sessionRegistry.GetByName("cmyui").Returns(existing);
		_users.FetchByNameAsync("cmyui").Returns((User?)null);
		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

		var result = await useCase.ExecuteAsync(request);

		_sessionRegistry.Received(1).Remove(existing);
		Assert.Equal("incorrect-credentials", result.OsuToken);
	}

	[Fact]
	public async Task DuplicateExpiredSession_RemovesOldSessionsBotSpectateRelationship()
	{
		// #spec_{userId} is keyed by the persistent user id, stable across relogins — without this
		// cleanup, a relogin would pile a dead member reference onto the previous session's channel.
		var bot = MakeBot();
		_sessionRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		var existing = new GameSession(1, "cmyui", "old-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			LastRecvTime = DateTimeOffset.UtcNow.AddSeconds(-100)
		};
		existing.AddSpectator(bot);
		bot.Spectating = existing;
		_sessionRegistry.GetByName("cmyui").Returns(existing);
		_users.FetchByNameAsync("cmyui").Returns((User?)null);
		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

		await useCase.ExecuteAsync(request);

		Assert.Empty(existing.Spectators);
		Assert.Null(bot.Spectating);
	}

	[Fact]
	public async Task DuplicateExpiredSession_InMatch_LeavesMatchAndFreesSlot()
	{
		// RC3: relogin eviction used to bypass PlayerLogoutService and just remove the session
		// from the registry, leaving its match slot occupied forever — GhostDisconnectService
		// only reaps sessions still present in a registry, so the slot could never be freed
		// (duplicate players after a taskkill reconnect, "match is locked", !mp make appearing
		// to kick its own creator via a stale Match reference).
		var match = new MatchSession(0, "test match", "", "Some Map", 100, new string('a', 32),
			999, GameMode.Standard, Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead,
			false, 0, "#multiplayer");
		var existing = new GameSession(1, "cmyui", "old-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			LastRecvTime = DateTimeOffset.UtcNow.AddSeconds(-100),
			Match = match
		};
		match.Slots[0].PlayerId = existing.Id;
		match.Slots[0].Status = SlotStatus.NotReady;
		_sessionRegistry.GetByName("cmyui").Returns(existing);
		_users.FetchByNameAsync("cmyui").Returns((User?)null);
		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

		await useCase.ExecuteAsync(request);

		Assert.True(match.Slots[0].Empty);
		Assert.Null(existing.Match);
	}

	[Fact]
	public async Task UnknownUsername_ReturnsIncorrectCredentials()
	{
		_users.FetchByNameAsync("cmyui").Returns((User?)null);
		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

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
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

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
		var request = new LoginRequest(LoginBody(osuVersion: "b20231231tourney"),
			IPAddress.Loopback);

		var result = await useCase.ExecuteAsync(request);

		Assert.Equal("no", result.OsuToken);
		Assert.Equal(ServerPacketWriter.LoginReply((int)LoginFailureReason.AuthenticationFailed), result.ResponseBody);
	}

	[Fact]
	public async Task NoGeolocationHeaders_SessionCountryComesFromStoredUserRecord()
	{
		// Country never comes from request headers — the session's country is the user's
		// already-stored country, regardless of which (if any) geolocation headers arrive.
		SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified, country: "jp");

		GameSession? captured = null;
		_sessionRegistry.TryAdd(Arg.Any<GameSession>()).Returns(ci =>
		{
			captured = ci.Arg<GameSession>();
			return true;
		});

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);
		await useCase.ExecuteAsync(request);

		Assert.NotNull(captured);
		Assert.Equal("jp", captured!.Country.ToAcronym());
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
				new PlayerClientHash(99, "p", "a", "u", "d", DateTime.UtcNow, 1, "banned-user",
					UserPrivileges.Verified)
			]);
		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

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
				new PlayerClientHash(99, "p", "a", "u", "d", DateTime.UtcNow, 1, "other-account",
					UserPrivileges.Unrestricted)
			]);
		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

		var result = await useCase.ExecuteAsync(request);

		Assert.Equal("osu-generated-token", result.OsuToken);
	}

	[Fact]
	public async Task HappyPath_UnrestrictedFirstLogin_GrantsVerifiedAndRegistersSession()
	{
		SetUpHappyPath(out var user, UserPrivileges.Unrestricted); // not Verified yet

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

		var result = await useCase.ExecuteAsync(request);

		Assert.Equal("osu-generated-token", result.OsuToken);

		var expectedHeader = Concat(
			ServerPacketWriter.ProtocolVersion(19),
			ServerPacketWriter.LoginReply(user.Id));
		Assert.Equal(expectedHeader, result.ResponseBody.Take(expectedHeader.Length).ToArray());

		// first login grants VERIFIED (user.Id=10 here, not FIRST_USER_ID=3, so no bonus staff privs)
		await _users.Received(1).UpdatePrivilegesAsync(user.Id, UserPrivileges.Unrestricted | UserPrivileges.Verified,
			Arg.Any<CancellationToken>());
		_sessionRegistry.Received(1)
			.TryAdd(Arg.Is<GameSession>(s => s != null && s.Id == user.Id && s.Token == "osu-generated-token"));
	}

	[Fact]
	public async Task HappyPath_BotStartsSpectatingTheNewSession()
	{
		var bot = MakeBot();
		_sessionRegistry.GetByUserId(BotBootstrapService.BotId).Returns(bot);
		SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

		GameSession? captured = null;
		_sessionRegistry.TryAdd(Arg.Any<GameSession>()).Returns(ci =>
		{
			captured = ci.Arg<GameSession>();
			return true;
		});

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);
		await useCase.ExecuteAsync(request);

		Assert.NotNull(captured);
		Assert.Contains(bot, captured!.Spectators);
	}

	[Fact]
	public async Task HappyPath_AlreadyVerifiedUser_DoesNotUpdatePrivileges()
	{
		SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

		await useCase.ExecuteAsync(request);

		await _users.DidNotReceive()
			.UpdatePrivilegesAsync(Arg.Any<int>(), Arg.Any<UserPrivileges>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HappyPath_RestrictedUser_ResponseContainsAccountRestrictedPacket()
	{
		SetUpHappyPath(out _, UserPrivileges.Verified); // no Unrestricted -> restricted

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);

		var result = await useCase.ExecuteAsync(request);

		// account_restricted has no payload -> its full wire bytes are a fixed constant, safe to search for.
		var restrictedPacketBytes = ServerPacketWriter.AccountRestricted();
		Assert.Contains(
			Convert.ToHexString(restrictedPacketBytes),
			Convert.ToHexString(result.ResponseBody));
	}

	[Fact]
	public async Task HappyPath_CachesAllModeStatsAndCountryOnSession()
	{
		SetUpHappyPath(out var user, UserPrivileges.Unrestricted | UserPrivileges.Verified);
		_userStatRepository.FetchAllForUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
		[
			new Stats(user.Id, GameMode.Standard, 100_000, 90_000, 50),
			new Stats(user.Id, GameMode.Taiko, 200_000, 180_000, 80)
		]);

		GameSession? captured = null;
		_sessionRegistry.TryAdd(Arg.Any<GameSession>()).Returns(ci =>
		{
			captured = ci.Arg<GameSession>();
			return true;
		});

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);
		await useCase.ExecuteAsync(request);

		Assert.NotNull(captured);
		Assert.Equal("us", captured!.Country.ToAcronym());
		Assert.Equal(2, captured.ModeStats.Count);
		Assert.Equal(user.Id, captured.ModeStats[GameMode.Standard].Rank);
		Assert.Equal(90_000, captured.ModeStats[GameMode.Standard].RankedScore);
	}

	[Fact]
	public async Task MotdText_WhenSet_SendsNotification()
	{
		_settings.GetAsync("Motd", Arg.Any<CancellationToken>()).Returns("Test message of the day!");
		SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);
		var result = await useCase.ExecuteAsync(request);

		var notificationPacket = ServerPacketWriter.Notification("Test message of the day!");
		var bodyHex = Convert.ToHexString(result.ResponseBody);
		Assert.Contains(Convert.ToHexString(notificationPacket), bodyHex);
	}

	[Fact]
	public async Task MotdText_WhenUnset_SendsNoNotification()
	{
		SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);
		var result = await useCase.ExecuteAsync(request);

		// No welcome notification: the response starts with ProtocolVersion + LoginReply directly.
		var expectedHeader = Concat(
			ServerPacketWriter.ProtocolVersion(19),
			ServerPacketWriter.LoginReply(10));
		Assert.Equal(expectedHeader, result.ResponseBody.Take(expectedHeader.Length).ToArray());
	}

	[Fact]
	public async Task MenuIconLocalPath_WhenSet_SendsMainMenuIconPacket()
	{
		_settings.GetAsync("MenuIcon:Path", Arg.Any<CancellationToken>())
			.Returns((string?)Path.Combine(AppContext.BaseDirectory, "Data", "MenuIcon.png"));
		_settings.GetAsync("MenuIcon:Url", Arg.Any<CancellationToken>()).Returns((string?)null);
		SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);
		var result = await useCase.ExecuteAsync(request);

		var expectedPacket = ServerPacketWriter.MainMenuIcon("https://api.test.local/menuicon/icon",
			"https://github.com/thnhmai06/osuBasil");
		var bodyHex = Convert.ToHexString(result.ResponseBody);
		Assert.Contains(Convert.ToHexString(expectedPacket), bodyHex);
	}

	[Fact]
	public async Task MenuIconExternalUrl_WhenSet_SendsMainMenuIconPacketWithThatUrl()
	{
		_settings.GetAsync("MenuIcon:Path", Arg.Any<CancellationToken>())
			.Returns((string?)"https://example.test/icon.png");
		_settings.GetAsync("MenuIcon:Url", Arg.Any<CancellationToken>()).Returns((string?)null);
		SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);
		var result = await useCase.ExecuteAsync(request);

		var expectedPacket = ServerPacketWriter.MainMenuIcon("https://example.test/icon.png",
			"https://github.com/thnhmai06/osuBasil");
		var bodyHex = Convert.ToHexString(result.ResponseBody);
		Assert.Contains(Convert.ToHexString(expectedPacket), bodyHex);
	}

	[Fact]
	public async Task MenuIconPath_WhenUnset_SendsNoMainMenuIconPacket()
	{
		// NSubstitute's default for an unconfigured Task<string?>-returning member is a completed
		// Task wrapping "", not null — stub both keys explicitly so "unset" here matches what the
		// real Settings-table-backed repository returns for a row whose Value is SQL NULL.
		_settings.GetAsync("MenuIcon:Path", Arg.Any<CancellationToken>()).Returns((string?)null);
		_settings.GetAsync("MenuIcon:Url", Arg.Any<CancellationToken>()).Returns((string?)null);
		SetUpHappyPath(out _, UserPrivileges.Unrestricted | UserPrivileges.Verified);

		var useCase = MakeUseCase();
		var request = new LoginRequest(LoginBody(), IPAddress.Loopback);
		var result = await useCase.ExecuteAsync(request);

		var unexpectedPacket = ServerPacketWriter.MainMenuIcon("https://api.test.local/menuicon/icon",
			"https://github.com/thnhmai06/osuBasil");
		var bodyHex = Convert.ToHexString(result.ResponseBody);
		Assert.DoesNotContain(Convert.ToHexString(unexpectedPacket), bodyHex);
	}

	/// <summary>
	///     Regression test: the channel-info packet broadcast to every other online session used to
	///     be rebuilt fresh per recipient even though its content (channel name/topic/player count)
	///     never varies by recipient — pure allocation waste scaling with online session count on
	///     every login. This pins that the broadcast content itself is unaffected by hoisting that
	///     packet build out of the per-recipient loop.
	/// </summary>
	[Fact]
	public async Task Login_BroadcastsChannelInfoToOtherOnlineSessions()
	{
		SetUpHappyPath(out _, UserPrivileges.Unrestricted);
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		_channelRegistry.AutoJoinChannels.Returns([channel]);
		var bystander =
			new GameSession(99, "bystander", "tok", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		_sessionRegistry.All.Returns([bystander]);

		var useCase = MakeUseCase();
		await useCase.ExecuteAsync(new LoginRequest(LoginBody(), IPAddress.Loopback));

		// Login also broadcasts the new session's presence/stats to every other online session
		// afterward, so the channel-info bytes only need to lead the bystander's queued packets.
		var expected = ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount);
		var actual = bystander.Dequeue();
		Assert.Equal(expected, actual.Take(expected.Length));
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
		_userStatRepository.FetchAllForUserAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
		_relationships.FetchAllAsync(userId, null, Arg.Any<CancellationToken>()).Returns([]);
		_tokenGenerator.GenerateToken().Returns("generated-token");
	}

	private static User MakeUser(int id, UserPrivileges priv, string country = "us")
	{
		return new User(id, "cmyui", Enum.Parse<Country>(country, true), priv, default);
	}

	private static byte[] Concat(params byte[][] parts)
	{
		return [.. parts.SelectMany(p => p)];
	}
}