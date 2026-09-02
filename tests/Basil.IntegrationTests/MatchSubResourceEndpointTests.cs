using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the `/matches/{matchId}/{hosts,refs,ban,kick,invite,slots,timer,abort,close}` routes.
///     Matches are created through the real `POST /matches` route (no chat "sender", host id 0), then
///     seated with real <see cref="UserSession" />s registered directly against the app's actual
///     DI-resolved <see cref="ISessionRegistry{GameSession}" />/<see cref="MatchMembershipService" /> —
///     the same production singletons the routes themselves use.
/// </summary>
public class MatchSubResourceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private const string AdminKey = "correct-key";
	private static readonly int[] InputValue = [555];
	private readonly WebApplicationFactory<Program> _factory;

	public MatchSubResourceEndpointTests(WebApplicationFactory<Program> factory)
	{
		_factory = factory.WithWebHostBuilder(builder =>
		{
			builder.ConfigureAppConfiguration((_, config) =>
			{
				config.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Basil:Server:Domain"] = "test.local",
					["Basil:Bot:CommandPrefix"] = "!"
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton(TestDoubles.FixedAdminKeySettingsRepository());
				services.AddSingleton<IMatchRepository>(new NoopMatchRepository());
				services.AddSingleton<IUserRepository>(new NoopUserRepository());
			});
		});
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = AdminKey)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	private static async Task<int> CreateMatchAsync(HttpClient client)
	{
		var request = MakeRequest(HttpMethod.Post, "/matches");
		request.Content = JsonContent.Create(new { });
		var response = await client.SendAsync(request);
		var created = await response.Content.ReadFromJsonAsync<JsonElement>();
		return created.GetProperty("data").GetProperty("id").GetInt32();
	}

	private async Task<GameSession> SeatNewPlayer(int id, string name, int matchId)
	{
		var sessionRegistry = _factory.Services.GetRequiredService<ISessionRegistry<GameSession>>();
		var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
		var matchMembership = _factory.Services.GetRequiredService<MatchMembershipService>();

		var session = new GameSession(id, name, $"token-{id}", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		sessionRegistry.TryAdd(session);

		var match = matchRegistry.GetByDbId(matchId)!;
		Assert.Equal(MatchMembershipService.JoinResult.Ok, await matchMembership.JoinAsync(session, match, ""));
		return session;
	}

	// ---- 404s for a match that isn't currently live ----

	[Theory]
	[InlineData("GET", "/matches/999999/hosts")]
	[InlineData("GET", "/matches/999999/refs")]
	[InlineData("GET", "/matches/999999/ban")]
	[InlineData("GET", "/matches/999999/slots")]
	[InlineData("GET", "/matches/999999/timer")]
	public async Task GetSubResource_UnknownMatch_ReturnsNotFound(string method, string path)
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(new HttpMethod(method), path));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	/// <summary>
	///     Regression test (Issue #4): a matchId overflowing int32 used to fail the built-in `:int`
	///     route constraint outright, surfacing as a bare, unenveloped 404 instead of a real error --
	///     the request never reached a handler or EnvelopeMiddleware at all. The `:numericid` constraint
	///     lets routing match on any all-digit id, so the framework's own model-binding failure (already
	///     a proper enveloped 400 for other cases) handles this one too.
	/// </summary>
	[Fact]
	public async Task GetMatch_OverflowingMatchId_ReturnsBadRequestNotBareNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Get, "/matches/99999999999999999999"));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task PutHosts_MissingAdminKey_ReturnsUnauthorized()
	{
		var request = MakeRequest(HttpMethod.Put, "/matches/1/hosts", null);
		request.Content = JsonContent.Create(new { userId = 1 });

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	// ---- /chat ----

	[Theory]
	[InlineData("POST", "/matches/1/chat")]
	[InlineData("GET", "/matches/1/chat/live")]
	public async Task Chat_MissingAdminKey_ReturnsUnauthorized(string method, string path)
	{
		var request = MakeRequest(new HttpMethod(method), path, null);
		if (method == "POST") request.Content = JsonContent.Create(new { text = "hello" });

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task SendChat_UnknownMatch_ReturnsNotFound()
	{
		var request = MakeRequest(HttpMethod.Post, "/matches/999999/chat");
		request.Content = JsonContent.Create(new { text = "hello" });

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task SendChat_BlankText_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var request = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/chat");
		request.Content = JsonContent.Create(new { text = "   " });
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task SendChat_MultilineText_BecomesOneMessagePerLine()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var sessionRegistry = _factory.Services.GetRequiredService<ISessionRegistry<GameSession>>();
		if (sessionRegistry.GetByUserId(BotBootstrapService.BotId) is null)
			sessionRegistry.TryAdd(new GameSession(BotBootstrapService.BotId, "BasilBot", "bot-token",
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch) { IsBot = true });

		var request = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/chat");
		request.Content = JsonContent.Create(new { text = "first\n\nsecond" });
		var response = await client.SendAsync(request);
		var view = await response.Content.ReadFromJsonAsync<JsonElement>();

		response.EnsureSuccessStatusCode();
		Assert.Equal(2, view.GetProperty("data").GetProperty("sent").GetInt32());
	}

	// ---- /hosts ----

	[Fact]
	public async Task Hosts_SetThenClear_ReflectsInGet()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var player = await SeatNewPlayer(2001, "hostcandidate", matchId);

		var putRequest = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/hosts");
		putRequest.Content = JsonContent.Create(new { userId = player.Id });
		var putResponse = await client.SendAsync(putRequest);
		putResponse.EnsureSuccessStatusCode();

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/hosts"));
		var view = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(player.Id, view.GetProperty("data").GetProperty("host").GetProperty("id").GetInt32());

		var deleteResponse = await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/hosts"));
		deleteResponse.EnsureSuccessStatusCode();

		var afterClear = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/hosts"));
		var clearedView = await afterClear.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(clearedView.GetProperty("data").GetProperty("host").ValueKind is JsonValueKind.Null);
	}

	// ---- /refs ----

	[Fact]
	public async Task Refs_PutToEmpty_ReturnsConflict()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/refs");
		request.Content = JsonContent.Create(new { userIds = Array.Empty<int>() });
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
	}

	[Fact]
	public async Task Refs_DeleteLastReferee_ReturnsConflict()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var referee = await SeatNewPlayer(2002, "onlyref", matchId);

		var putRequest = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/refs");
		putRequest.Content = JsonContent.Create(new { userIds = new[] { referee.Id } });
		(await client.SendAsync(putRequest)).EnsureSuccessStatusCode();

		var deleteRequest = MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/refs");
		deleteRequest.Content = JsonContent.Create(new { userIds = new[] { referee.Id } });
		var deleteResponse = await client.SendAsync(deleteRequest);

		deleteResponse.EnsureSuccessStatusCode();
		var results = await deleteResponse.Content.ReadFromJsonAsync<JsonElement>();
		var result = results.GetProperty("data").EnumerateArray().Single();
		Assert.False(result.GetProperty("ok").GetBoolean());
		Assert.Equal("Refusing to leave the match with no referees.", result.GetProperty("error").GetString());
	}

	[Fact]
	public async Task Refs_Patch_NeverConflicts()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var referee = await SeatNewPlayer(2003, "patchedref", matchId);

		var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/refs");
		request.Content = JsonContent.Create(new { userIds = new[] { referee.Id } });
		var response = await client.SendAsync(request);

		response.EnsureSuccessStatusCode();
		var view = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Contains(view.GetProperty("data").GetProperty("referees").EnumerateArray(),
			r => r.GetProperty("id").GetInt32() == referee.Id);
	}

	// ---- /ban ----

	[Fact]
	public async Task Ban_PutToEmpty_Succeeds_NoGuard()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/ban");
		request.Content = JsonContent.Create(new { userIds = Array.Empty<int>() });
		var response = await client.SendAsync(request);

		response.EnsureSuccessStatusCode();
	}

	/// <summary>Regression test (Issue #4): a userId that belongs to no registered account must be rejected.</summary>
	[Fact]
	public async Task Ban_Patch_UnregisteredUserId_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
		request.Content = JsonContent.Create(new { userIds = new[] { 424242 } });
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task Ban_PatchThenUnban_ReflectsInGet()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		((NoopUserRepository)_factory.Services.GetRequiredService<IUserRepository>())
			.Add(new User(555, "offline", Country.Xx, UserPrivileges.Unrestricted, default));

		var patchRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
		patchRequest.Content = JsonContent.Create(new { userIds = InputValue });
		(await client.SendAsync(patchRequest)).EnsureSuccessStatusCode();

		var afterBan = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/ban"));
		var bannedView = await afterBan.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Contains(bannedView.GetProperty("data").GetProperty("bannedUsers").EnumerateArray(),
			u => u.GetProperty("id").GetInt32() == 555);

		var unbanRequest = MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/ban");
		unbanRequest.Content = JsonContent.Create(new { userIds = new[] { 555 } });
		var unbanResponse = await client.SendAsync(unbanRequest);
		unbanResponse.EnsureSuccessStatusCode();
		var unbanResults = await unbanResponse.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(unbanResults.GetProperty("data").EnumerateArray().Single().GetProperty("ok").GetBoolean());

		var unbanAgainRequest = MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/ban");
		unbanAgainRequest.Content = JsonContent.Create(new { userIds = new[] { 555 } });
		var unbanAgainResponse = await client.SendAsync(unbanAgainRequest);
		unbanAgainResponse.EnsureSuccessStatusCode();
		var unbanAgainResults = await unbanAgainResponse.Content.ReadFromJsonAsync<JsonElement>();
		Assert.False(unbanAgainResults.GetProperty("data").EnumerateArray().Single().GetProperty("ok").GetBoolean());
	}

	// ---- /kick ----

	[Fact]
	public async Task Kick_SeatedPlayer_Returns204AndRemovesFromMatch()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var player = await SeatNewPlayer(2004, "kickme", matchId);
		((NoopUserRepository)_factory.Services.GetRequiredService<IUserRepository>())
			.Add(new User(player.Id, player.Name, Country.Xx, UserPrivileges.Unrestricted, default));

		var request = MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/slots");
		request.Content = JsonContent.Create(new { userId = player.Id });
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Null(player.Match);
	}

	[Fact]
	public async Task Kick_TargetNotInMatch_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var player = await SeatNewPlayer(2005, "elsewhere", matchId);
		var sessionRegistry = _factory.Services.GetRequiredService<ISessionRegistry<GameSession>>();
		var matchMembership = _factory.Services.GetRequiredService<MatchMembershipService>();
		var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
		await matchMembership.LeaveAsync(player, matchRegistry.GetByDbId(matchId)!);

		var request = MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/slots");
		request.Content = JsonContent.Create(new { userId = player.Id });
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		_ = sessionRegistry; // keep the DI resolution above self-documenting even though unused after Leave
	}

	// ---- /invite ----

	[Fact]
	public async Task Invite_Force_SeatsBannedTargetIsRejected_ButUnbannedTargetSeated()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var sessionRegistry = _factory.Services.GetRequiredService<ISessionRegistry<GameSession>>();
		var banned = new GameSession(2006, "banned", "t2006", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		var free = new GameSession(2007, "free", "t2007", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
		sessionRegistry.TryAdd(banned);
		sessionRegistry.TryAdd(free);
		((NoopUserRepository)_factory.Services.GetRequiredService<IUserRepository>())
			.Add(new User(banned.Id, banned.Name, Country.Xx, UserPrivileges.Unrestricted, default));

		var banRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
		banRequest.Content = JsonContent.Create(new { userIds = new[] { banned.Id } });
		(await client.SendAsync(banRequest)).EnsureSuccessStatusCode();

		var inviteRequest = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/slots");
		inviteRequest.Content = JsonContent.Create(new { userIds = new[] { banned.Id, free.Id }, force = true });
		var response = await client.SendAsync(inviteRequest);
		response.EnsureSuccessStatusCode();

		var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
		var byUserId = envelope.GetProperty("data").EnumerateArray()
			.ToDictionary(r => r.GetProperty("userId").GetInt32());

		Assert.False(byUserId[banned.Id].GetProperty("ok").GetBoolean());
		Assert.True(byUserId[free.Id].GetProperty("ok").GetBoolean());
		Assert.Null(banned.Match);
		Assert.NotNull(free.Match);
	}

	// ---- /slots ----

	[Fact]
	public async Task Slots_Get_ReturnsSixteenIndexedEntries()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/slots"));
		response.EnsureSuccessStatusCode();
		var body = await response.Content.ReadFromJsonAsync<JsonElement>();

		var slots = body.GetProperty("data").GetProperty("slots").EnumerateArray().ToList();
		Assert.Equal(16, slots.Count);
		for (var i = 0; i < 16; i++) Assert.Equal(i, slots[i].GetProperty("index").GetInt32());
	}

	/// <summary>
	///     Regression test (Issue #4): an empty slot used to serialize `team`/`mods`/`ready`/`loaded`
	///     as meaningless default values (0/false) alongside `user: null`. It now omits all four
	///     fields entirely -- `status` stays, since Open/Locked is still meaningful without an
	///     occupant.
	/// </summary>
	[Fact]
	public async Task Slots_Get_EmptySlot_OmitsOccupantOnlyFields()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/matches/{matchId}/slots"));
		response.EnsureSuccessStatusCode();
		var body = await response.Content.ReadFromJsonAsync<JsonElement>();

		var slot = body.GetProperty("data").GetProperty("slots")[0];
		Assert.Equal(JsonValueKind.Null, slot.GetProperty("user").ValueKind);
		Assert.True(slot.TryGetProperty("status", out _));
		Assert.False(slot.TryGetProperty("team", out _));
		Assert.False(slot.TryGetProperty("mods", out _));
		Assert.False(slot.TryGetProperty("ready", out _));
		Assert.False(slot.TryGetProperty("loaded", out _));
	}

	[Fact]
	public async Task Slots_Put_SwapsTwoOccupants()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var a = await SeatNewPlayer(2008, "playerA", matchId);
		var b = await SeatNewPlayer(2009, "playerB", matchId);

		var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
		var match = matchRegistry.GetByDbId(matchId)!;
		var slotA = match.GetSlotId(a.Id)!.Value;
		var slotB = match.GetSlotId(b.Id)!.Value;

		var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/slots");
		request.Content = JsonContent.Create(new
		{
			slots = new object[]
			{
				new { index = slotA, userId = b.Id },
				new { index = slotB, userId = a.Id }
			}
		});
		var response = await client.SendAsync(request);

		response.EnsureSuccessStatusCode();
		Assert.Equal(b.Id, match.Slots[slotA].PlayerId);
		Assert.Equal(a.Id, match.Slots[slotB].PlayerId);
	}

	[Fact]
	public async Task Slots_Put_UnknownUserId_ReturnsConflict()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/slots");
		request.Content = JsonContent.Create(new
		{
			slots = new object[] { new { index = 0, userId = 999999 } }
		});
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
	}

	[Fact]
	public async Task Slots_UserIdAndLockedTogether_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var player = await SeatNewPlayer(2010, "lockedplayer", matchId);
		var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
		var slot = matchRegistry.GetByDbId(matchId)!.GetSlotId(player.Id)!.Value;

		var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/slots");
		request.Content = JsonContent.Create(new
		{
			slots = new object[] { new { index = slot, userId = player.Id, locked = true } }
		});
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	// ---- /timer ----

	[Fact]
	public async Task Timer_StartFalse_SetsRunningTrue_ThenDeleteAborts()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var startRequest = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/timer");
		startRequest.Content = JsonContent.Create(new { seconds = 120 });
		var startResponse = await client.SendAsync(startRequest);
		startResponse.EnsureSuccessStatusCode();
		var afterStart = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
		var afterStartData = afterStart.GetProperty("data");
		Assert.True(afterStartData.GetProperty("running").GetBoolean());
		Assert.False(afterStartData.GetProperty("autoStart").GetBoolean());

		var abortResponse = await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/timer"));
		abortResponse.EnsureSuccessStatusCode();

		var secondAbort = await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/timer"));
		Assert.Equal(HttpStatusCode.Conflict, secondAbort.StatusCode);
	}

	// ---- /abort, /close ----

	[Fact]
	public async Task Abort_NotInProgress_ReturnsConflict()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, $"/matches/{matchId}/abort"));

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
	}

	[Fact]
	public async Task Close_Returns204AndRemovesFromRegistry()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, $"/matches/{matchId}/close"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
		Assert.Null(matchRegistry.GetByDbId(matchId));
	}

	/// <summary>Auto-incrementing in-memory stand-in for the Matches/Rounds tables — nothing persisted.</summary>
	private sealed class NoopMatchRepository : IMatchRepository
	{
		private int _nextId = 1;

		public Task<int> CreateMatchAsync(string name, DateTime createdAt,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_nextId++);
		}

		public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<int> CreateRoundAsync(int matchId, int roundIndex, string mapMd5,
			GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
			Mods mods, DateTime startedAt, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<Match?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Match?>(null);
		}

		public Task<IReadOnlyList<Round>> FetchRoundsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Round>>([]);
		}

		public Task<IReadOnlyList<Match>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Match>>([]);
		}

		public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task CreateEventAsync(MatchEvent row, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<MatchEvent>> FetchEventsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<MatchEvent>>([]);
		}

		public Task<IReadOnlyList<Match>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Match>>([]);
		}

		public Task<IReadOnlyList<Round>> FetchUnrecoveredRoundsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Round>>([]);
		}
	}

	/// <summary>
	///     Stands in for the real DB-backed <see cref="IUserRepository" /> so an offline/unregistered id
	///     referenced by these tests (e.g. a banned id that was never seated) resolves to "no account" —
	///     UserBriefResolver's documented fallback — instead of hitting the real SQLite path these tests
	///     otherwise never need a working database connection for.
	/// </summary>
	private sealed class NoopUserRepository : IUserRepository
	{
		private readonly Dictionary<int, User> _byId = new();

		public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_byId.GetValueOrDefault(id));
		}

		public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_byId.Values.FirstOrDefault(u => u.Name == name));
		}

		public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(null);
		}

		public Task UpdateCountryAsync(int id, Country country, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task UpdatePrivilegesAsync(int id, UserPrivileges privilege,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<User?> CreateAsync(string name, string pwBcrypt, Country country, UserPrivileges? privilege = null,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<User?>(null);
		}

		public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<User>>([]);
		}

		/// <summary>Seeds a user row so a kick/ban route resolving it by id or name finds a real account.</summary>
		public void Add(User user)
		{
			_byId[user.Id] = user;
		}
	}
}