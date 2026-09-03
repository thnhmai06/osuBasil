using System.Net.Http.Headers;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Configurations;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Multiplayer;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the live SSE layer end to end over TestServer's in-memory HTTP transport — no real
///     osu! client or tourney manager involved, but a real streamed GET request, a real
///     IMatchLiveEvents/IPlayerInputEvents publish, and a real incremental read of the response
///     body. Publishes are retried in a short poll loop rather than fired once: a client's request
///     completing does not guarantee the server-side handler has reached the event subscription yet
///     (both run in-process with no real network latency between them, so this race is easy to lose
///     without the retry).
/// </summary>
public class LiveSseEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;

	public LiveSseEndpointTests(WebApplicationFactory<Program> factory)
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
				services.AddSingleton(TestDoubles.BypassAdminKeySettingsRepository());
				// A stub avoids needing a real SQLite file for these plumbing tests. IMatchRegistry.CreateAsync
				// persists a row for every match it registers, so this stub must actually complete
				// (not throw) for RegisterLiveMatch to work — it just never returns anything durable.
				services.AddSingleton<IMatchRepository>(new NeverPersistedMatchRepository());
			});
		});
	}

	[Fact]
	public async Task MainChannel_ReceivesWhateverIsPublishedForThatMatchId()
	{
		var matchId = await RegisterLiveMatch();
		var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

		var (eventType, data, _, _) = await ReceiveAfterPublishAsync($"/matches/{matchId}/live",
			() => events.PublishMain(matchId, JsonSerializer.SerializeToUtf8Bytes(new { hello = "world" })));

		Assert.Equal("main", eventType);
		Assert.Contains("world", data);
	}

	/// <summary>
	///     Regression test (user-directed follow-up: "use every native SSE feature -- event, data,
	///     retry, id"): a stream built on the <c>Subscribe</c> helper (chat, and this standalone
	///     per-player input stream) is entirely event-oriented (ADR-004), so every item gets both an
	///     SSE <c>retry:</c> hint and a monotonic <c>id:</c>.
	/// </summary>
	[Fact]
	public async Task SpecChannel_ReceivesWhateverIsPublishedForThatPlayerId()
	{
		var events = _factory.Services.GetRequiredService<IPlayerInputEvents>();

		// discardFirst: true — the stream now leads with a `status` snapshot (this test's player is
		// offline, so `{"online":false,...}`) before any `input` this test publishes.
		var (eventType, data, eventId, retry) = await ReceiveAfterPublishAsync("/users/7/live",
			() => events.PublishInput(7, [.. "frame-data"u8]), true);

		Assert.Equal("input", eventType);
		Assert.Equal("frame-data", data);
		Assert.Equal("5000", retry);
		Assert.Equal("1", eventId);
	}

	/// <summary>
	///     Regression test (Issue #4: "GET /users/{id}/live should include status information in
	///     addition to spectating information"): the stream leads with a `status` snapshot even for a
	///     player who has never been online.
	/// </summary>
	[Fact]
	public async Task SpecChannel_InitialSnapshotReportsOffline()
	{
		var client = _factory.CreateClient();
		var request = new HttpRequestMessage(HttpMethod.Get, "/users/7/live") { Headers = { Host = "api.test.local" } };
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		var (eventType, data, _, _) = await ConnectAndReadOneEventAsync(client, request, false, cts.Token);

		Assert.Equal("status", eventType);
		Assert.Contains("\"online\":false", data);
	}

	/// <summary>
	///     Regression test (Issue #4: "GET /users/{id}/live should include status information"): a
	///     status change (login, activity update) reaches an already-connected stream as a `status`
	///     event, separate from `input`.
	/// </summary>
	[Fact]
	public async Task SpecChannel_ReceivesPublishedStatusChanges()
	{
		var events = _factory.Services.GetRequiredService<IPlayerStatusEvents>();

		var (eventType, data, _, retry) = await ReceiveAfterPublishAsync("/users/7/live",
			() => events.PublishStatus(7, [.. "{\"online\":true,\"activity\":2}"u8]), true);

		Assert.Equal("status", eventType);
		Assert.Equal("{\"online\":true,\"activity\":2}", data);
		Assert.Equal("5000", retry);
	}

	[Fact]
	public async Task SpecChannel_IgnoresPublishesForOtherPlayerIds()
	{
		var events = _factory.Services.GetRequiredService<IPlayerInputEvents>();

		var (_, data, _, _) = await ReceiveAfterPublishAsync("/users/7/live", () =>
		{
			events.PublishInput(8, [.. "not for userSession 7"u8]);
			events.PublishInput(7, [.. "for userSession 7"u8]);
		}, true);

		Assert.Equal("for userSession 7", data);
	}

	[Fact]
	public async Task MainChannel_OnlyReceivesPublishesForItsOwnMatchId_NotOtherMatches()
	{
		var matchId = await RegisterLiveMatch();
		var events = _factory.Services.GetRequiredService<IMatchLiveEvents>();

		var (_, data, _, _) = await ReceiveAfterPublishAsync($"/matches/{matchId}/live", () =>
		{
			events.PublishMain(12, [.. "wrong match"u8]);
			events.PublishMain(matchId, [.. "right match"u8]);
		});

		Assert.Equal("right match", data);
	}

	/// <summary>
	///     Directly registers a live <see cref="MatchSession" /> against the real, DI-resolved
	///     <see cref="IMatchRegistry" /> — this test file has no admin key configured (it only cares
	///     about the SSE plumbing, not the write routes), so matches can't be created through
	///     `POST /matches`, but `GET /matches/{matchId}/live` returns 409 unless the match is actually
	///     tracked in memory. Returns the real <see cref="MatchSession.DbId" /> the registry assigned
	///     — it can't be overridden to an arbitrary value without desyncing the registry's own
	///     DbId-to-wire-id lookup.
	/// </summary>
	private async Task<int> RegisterLiveMatch()
	{
		var matchRegistry = _factory.Services.GetRequiredService<IMatchRegistry>();
		var data = new MatchState(0, false, 0, 0, "Test Match", "", "", 0, "", [], [], [], 0, 0, 0, 0, false, [],
			0);
		var match = await matchRegistry.CreateAsync(data, 0);
		return match.DbId;
	}

	/// <summary>
	///     Connects a streamed GET to <paramref name="path" /> with an EventSource-style Accept
	///     header, calls <paramref name="publish" /> repeatedly (every 50ms) until the next SSE
	///     message arrives or 10s elapse, and returns that message's `event:`/`data:` lines.
	///     Publishing has to keep running across BOTH the initial connect and the first read — an
	///     SSE response apparently doesn't flush its headers until its first write, so awaiting
	///     SendAsync before ever publishing anything would deadlock (nothing would ever trigger that
	///     first write).
	/// </summary>
	private async Task<(string? EventType, string Data, string? EventId, string? Retry)> ReceiveAfterPublishAsync(
		string path, Action publish, bool discardFirst = false)
	{
		var client = _factory.CreateClient();
		var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var pipelineTask = ConnectAndReadOneEventAsync(client, request, discardFirst, cts.Token);

		while (!pipelineTask.IsCompleted)
		{
			publish();
			await Task.WhenAny(pipelineTask, Task.Delay(50, CancellationToken.None));
		}

		return await pipelineTask;
	}

	private static async Task<(string? EventType, string Data, string? EventId, string? Retry)>
		ConnectAndReadOneEventAsync(
			HttpClient client, HttpRequestMessage request, bool discardFirst, CancellationToken cancellationToken)
	{
		using var response =
			await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var reader = new StreamReader(stream);

		if (discardFirst) await ReadNextEventAsync(reader, cancellationToken);
		return await ReadNextEventAsync(reader, cancellationToken);
	}

	/// <summary>
	///     Reads one complete SSE event (every field line up to the terminating blank line) --
	///     .NET's <c>SseFormatter</c> writes <c>event:</c>/<c>data:</c> first and <c>id:</c>/<c>retry:</c>
	///     after, so this cannot return as soon as <c>data:</c> is seen the way a data-only reader could.
	/// </summary>
	private static async Task<(string? EventType, string Data, string? EventId, string? Retry)> ReadNextEventAsync(
		StreamReader reader, CancellationToken cancellationToken)
	{
		string? eventType = null;
		string? data = null;
		string? eventId = null;
		string? retry = null;
		while (true)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line is null) throw new IOException("Stream ended unexpectedly.");

			if (line.Length == 0)
			{
				if (data is not null) return (eventType, data, eventId, retry);
				continue;
			}

			if (line.StartsWith("event: ", StringComparison.Ordinal))
				eventType = line["event: ".Length..];
			else if (line.StartsWith("id: ", StringComparison.Ordinal))
				eventId = line["id: ".Length..];
			else if (line.StartsWith("retry: ", StringComparison.Ordinal))
				retry = line["retry: ".Length..];
			else if (line.StartsWith("data: ", StringComparison.Ordinal))
				data = line["data: ".Length..];
		}
	}

	/// <summary>Completes every call trivially — nothing is actually durable, exactly what these tests need.</summary>
	private sealed class NeverPersistedMatchRepository : IMatchRepository
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
}