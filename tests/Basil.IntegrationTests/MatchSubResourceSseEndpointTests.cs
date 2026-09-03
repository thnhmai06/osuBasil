using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Formats;
using Basil.Application.Services;
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
///     Covers the five new SSE channels added alongside the `/hosts`, `/refs`, `/ban`, `/slots`, and
///     `/timer` sub-resource routes — full-then-delta, same pattern as
///     <see cref="MatchLiveChannelsEndpointTests" />. Each test connects the stream first, then
///     drives the corresponding write route (through real DI-resolved production singletons, exactly
///     like <see cref="MatchSubResourceEndpointTests" />) and asserts the resulting delta.
/// </summary>
public class MatchSubResourceSseEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private const string AdminKey = "correct-key";
	private static readonly int[] InputValue = [111];
	private readonly WebApplicationFactory<Program> _factory;

	public MatchSubResourceSseEndpointTests(WebApplicationFactory<Program> factory)
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
				services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
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

	/// <summary>
	///     Connects an SSE stream and reads its first event, then performs <paramref name="trigger" />
	///     and reads the event after that. The channel must already have a non-null
	///     <see cref="SnapshotChannel{T}.Latest" /> before connecting (callers warm it up with one
	///     preliminary write) — otherwise the connect's "subscribe, drain, snapshot" sequence has
	///     nothing to write immediately, and per the existing note on
	///     <see cref="MatchLiveChannelsEndpointTests" />'s own helper ("an SSE response apparently
	///     doesn't flush its headers until its first write"), awaiting the connect before ever
	///     publishing anything would deadlock. With a warm channel, the connect's first event is that
	///     warm full snapshot, and <paramref name="trigger" /> then produces a real delta as the second.
	/// </summary>
	private async Task<(string? EventType, string Data)> ReceiveAfterTriggerAsync(string path, Func<Task> trigger)
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var client = _factory.CreateClient();
		var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
		using var reader = new StreamReader(stream);

		await ReadNextEventAsync(reader, cts.Token); // discard the warm full snapshot
		await trigger();
		return await ReadNextEventAsync(reader, cts.Token);
	}

	/// <summary>
	///     Like <see cref="ReceiveAfterTriggerAsync" /> but for a stream with no warm-snapshot
	///     precondition (event-oriented, nothing to send before the first real event) that also
	///     requires the admin key: connects, primes the connection until it flushes, discards that
	///     priming event, then performs <paramref name="trigger" /> and reads the event it produces.
	/// </summary>
	/// <remarks>
	///     A purely event-oriented stream flushes no headers until its first item is actually written,
	///     so simply awaiting the connect before publishing anything would deadlock -- the connect
	///     never completes on its own. <paramref name="prime" /> fires repeatedly (each firing that
	///     lands before the endpoint's subscription is registered, or before a buffering stream's next
	///     flush, is silently absorbed into that first flush) until the connect finally completes.
	///     Both callbacks must not depend on an HTTP round trip through this same
	///     <see cref="WebApplicationFactory{T}" /> -- an in-process DI call is safe, and safe to repeat
	///     for <paramref name="prime" />.
	/// </remarks>
	private async Task<(string? EventType, string Data)> ReceiveAfterTriggerAsyncNoWarmup(string path,
		Func<Task> prime, Func<Task> trigger)
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var client = _factory.CreateClient();
		var request = new HttpRequestMessage(HttpMethod.Get, path)
		{
			Headers = { Host = "api.test.local", Authorization = new AuthenticationHeaderValue("Bearer", AdminKey) }
		};
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

		// Each retry waits a full flush cycle (plus margin) rather than polling tightly: a
		// buffering stream's first flush can take up to its flush interval even after the
		// subscription registers, so a short poll would fire many redundant primes into that
		// same pending flush before noticing it landed.
		var connectTask = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
		while (!connectTask.IsCompleted)
		{
			await prime();
			await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromMilliseconds(1200), cts.Token));
		}

		using var response = await connectTask;
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
		using var reader = new StreamReader(stream);

		await ReadNextEventAsync(reader, cts.Token); // discard the priming flush
		await trigger();
		return await ReadNextEventAsync(reader, cts.Token);
	}

	/// <summary>Connects an SSE stream and returns its first event -- the warm full snapshot, unread.</summary>
	/// <remarks>Same "channel must already be warm" precondition as <see cref="ReceiveAfterTriggerAsync" />.</remarks>
	private async Task<(string? EventType, string Data)> ConnectAndReadOneEventAsync(string path)
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var client = _factory.CreateClient();
		var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
		using var reader = new StreamReader(stream);

		return await ReadNextEventAsync(reader, cts.Token);
	}

	private static async Task<(string? EventType, string Data)> ReadNextEventAsync(StreamReader reader,
		CancellationToken cancellationToken)
	{
		string? eventType = null;
		while (true)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line is null) throw new IOException("Stream ended unexpectedly.");

			if (line.StartsWith("event: ", StringComparison.Ordinal))
				eventType = line["event: ".Length..];
			else if (line.StartsWith("data: ", StringComparison.Ordinal))
				return (eventType, line["data: ".Length..]);
		}
	}

	[Fact]
	public async Task Hosts_Sse_DeltaFiresOnSetHost()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var warmHost = await SeatNewPlayer(3000, "warmhost", matchId);
		var player = await SeatNewPlayer(3001, "hostcandidate", matchId);

		// Warm HostSnapshot.Latest so the SSE connect below has an immediate full snapshot to write —
		// see ReceiveAfterTriggerAsync's doc comment for why an unwarmed channel would deadlock.
		var warmRequest = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/hosts");
		warmRequest.Content = JsonContent.Create(new { userId = warmHost.Id });
		(await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

		var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/hosts/live", async () =>
		{
			var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/hosts");
			request.Content = JsonContent.Create(new { userId = player.Id });
			(await client.SendAsync(request)).EnsureSuccessStatusCode();
		});

		Assert.Equal("hosts", eventType);
		Assert.Contains(player.Id.ToString(), data);
	}

	[Fact]
	public async Task Refs_Sse_DeltaFiresOnAddReferee()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var warmRef = await SeatNewPlayer(3010, "warmref", matchId);
		var referee = await SeatNewPlayer(3002, "newref", matchId);

		var warmRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/refs");
		warmRequest.Content = JsonContent.Create(new { userIds = new[] { warmRef.Id } });
		(await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

		var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/refs/live", async () =>
		{
			var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/refs");
			request.Content = JsonContent.Create(new { userIds = new[] { referee.Id } });
			(await client.SendAsync(request)).EnsureSuccessStatusCode();
		});

		Assert.Equal("refs", eventType);
		Assert.Contains(referee.Id.ToString(), data);
	}

	[Fact]
	public async Task Ban_Sse_DeltaFiresOnBan()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var userRepository = (NoopUserRepository)_factory.Services.GetRequiredService<IUserRepository>();
		userRepository.Add(new User(111, "warm", Country.Xx, UserPrivileges.Unrestricted, default));
		userRepository.Add(new User(777, "target", Country.Xx, UserPrivileges.Unrestricted, default));

		var warmRequest = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
		warmRequest.Content = JsonContent.Create(new { userIds = InputValue });
		(await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

		var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/ban/live", async () =>
		{
			var request = MakeRequest(HttpMethod.Patch, $"/matches/{matchId}/ban");
			request.Content = JsonContent.Create(new { userIds = new[] { 777 } });
			(await client.SendAsync(request)).EnsureSuccessStatusCode();
		});

		Assert.Equal("ban", eventType);
		Assert.Contains("777", data);
	}

	[Fact]
	public async Task Slots_Sse_DeltaFiresOnReassignment()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var player = await SeatNewPlayer(3003, "mover", matchId);
		var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
		var match = matchRegistry.GetByDbId(matchId)!;
		var currentSlot = match.GetSlotId(player.Id)!.Value;
		var otherSlot = currentSlot == 0 ? 1 : 0;

		// Warm SlotsSnapshot.Latest with a no-op re-team of the userSession's own current slot.
		var warmRequest = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/slots");
		warmRequest.Content = JsonContent.Create(new
		{
			slots = new[] { new { index = currentSlot + 1, userId = player.Id } }
		});
		(await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

		var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/slots/live", async () =>
		{
			var request = MakeRequest(HttpMethod.Put, $"/matches/{matchId}/slots");
			request.Content = JsonContent.Create(new
			{
				slots = new[] { new { index = otherSlot + 1, userId = player.Id } }
			});
			(await client.SendAsync(request)).EnsureSuccessStatusCode();
		});

		Assert.Equal("slots", eventType);
		Assert.Contains(player.Id.ToString(), data);
	}

	[Fact]
	public async Task Timer_Sse_DeltaFiresOnAbort()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		// Warm TimerSnapshot.Latest by starting a countdown (BeginCountdown publishes synchronously
		// before the POST response returns).
		var warmRequest = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/timer");
		warmRequest.Content = JsonContent.Create(new { seconds = 120 });
		(await client.SendAsync(warmRequest)).EnsureSuccessStatusCode();

		var (eventType, data) = await ReceiveAfterTriggerAsync($"/matches/{matchId}/timer/live",
			async () => (await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/matches/{matchId}/timer")))
				.EnsureSuccessStatusCode());

		Assert.Equal("timer", eventType);
		Assert.Contains("\"running\":false", data);
	}

	/// <summary>
	///     Regression test (Issue #4): "Remove secondsRemaining from the timer SSE stream to prevent
	///     network spam; client calculation should rely on startTime and endTime."
	/// </summary>
	[Fact]
	public async Task Timer_Sse_InitialSnapshotOmitsSecondsRemaining()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);

		var startRequest = MakeRequest(HttpMethod.Post, $"/matches/{matchId}/timer");
		startRequest.Content = JsonContent.Create(new { seconds = 120 });
		(await client.SendAsync(startRequest)).EnsureSuccessStatusCode();

		var (eventType, data) = await ConnectAndReadOneEventAsync($"/matches/{matchId}/timer/live");

		Assert.Equal("timer", eventType);
		Assert.DoesNotContain("secondsRemaining", data);
		Assert.Contains("\"startTime\"", data);
	}

	/// <summary>
	///     Regression test (Issue #4): "Buffer messages per connection and send them every second to
	///     reduce load." Two chat lines from the same request publish an instant apart, well inside the
	///     flush window, so they arrive as one combined `chat` event carrying both instead of two
	///     separate events.
	/// </summary>
	[Fact]
	public async Task Chat_Sse_BuffersLinesFromOneRequestIntoOneFlush()
	{
		var client = _factory.CreateClient();
		var matchId = await CreateMatchAsync(client);
		var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();
		var sender = new UserBrief(1, "Alice", Country.Us);

		var (eventType, data) = await ReceiveAfterTriggerAsyncNoWarmup($"/matches/{matchId}/chat/live",
			() =>
			{
				events.PublishChat(matchId,
					JsonSerializer.SerializeToUtf8Bytes(new MatchChatMessage(sender, "priming",
						DateTimeOffset.UtcNow), BasilJsonOptions.Instance));
				return Task.CompletedTask;
			},
			() =>
			{
				events.PublishChat(matchId,
					JsonSerializer.SerializeToUtf8Bytes(new MatchChatMessage(sender, "first",
						DateTimeOffset.UtcNow), BasilJsonOptions.Instance));
				events.PublishChat(matchId,
					JsonSerializer.SerializeToUtf8Bytes(new MatchChatMessage(sender, "second",
						DateTimeOffset.UtcNow), BasilJsonOptions.Instance));
				return Task.CompletedTask;
			});

		Assert.Equal("chat", eventType);
		var messages = JsonSerializer.Deserialize<JsonElement[]>(data);
		Assert.Equal(2, messages!.Length);
		Assert.Equal("first", messages[0].GetProperty("text").GetString());
		Assert.Equal("second", messages[1].GetProperty("text").GetString());
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
	///     referenced by these tests resolves to "no account" — UserBriefResolver's documented fallback —
	///     instead of hitting the real SQLite path these tests otherwise never need a working database
	///     connection for.
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

		/// <summary>Registers a user so <see cref="FetchByIdAsync" /> resolves it instead of "no account".</summary>
		public void Add(User user)
		{
			_byId[user.Id] = user;
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

		public Task SoftDeleteAsync(int id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default)
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
	}
}