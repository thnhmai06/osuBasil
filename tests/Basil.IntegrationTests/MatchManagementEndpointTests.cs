using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the new `/matches` list/create/settings/action routes end to end — in particular, this is
///     the first real endpoint <see cref="Basil.Web.Auth.AdminKeyAuthenticationHandler" />'s
///     `RequireAuthorization` policy is actually attached to, so the missing/wrong-key -&gt; 401 path
///     is verified through the full middleware pipeline here, not just the handler in isolation.
/// </summary>
public class MatchManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private const string AdminKey = "correct-key";
	private readonly WebApplicationFactory<Program> _factory;

	public MatchManagementEndpointTests(WebApplicationFactory<Program> factory)
	{
		// Minimal in-memory fake so CreateMatchAsync/FetchAllMatchesAsync/DeleteMatchAsync behave
		// realistically without a real SQLite file.
		var matches = new Dictionary<int, Match>();
		var nextId = 1;
		var matchPersistence = Substitute.For<IMatchRepository>();
		// Never exercised by these tests -- throw, matching the old fake, instead of the NSubstitute
		// default of a silently-completed Task<0>.
		matchPersistence.CreateRoundAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<GameMode>(),
				Arg.Any<MatchWinCondition>(), Arg.Any<MatchTeamType>(), Arg.Any<Mods>(), Arg.Any<DateTime>(),
				Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		matchPersistence.SetRoundEndedAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<bool>(),
				Arg.Any<CancellationToken>())
			.Throws(new NotSupportedException());
		matchPersistence.CreateMatchAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var id = nextId++;
				matches[id] = new Match(id, call.ArgAt<string>(0), call.ArgAt<DateTime>(1), null);
				return id;
			});
		matchPersistence.WhenForAnyArgs(m => m.SetMatchEndedAsync(default, default))
			.Do(call =>
			{
				if (matches.TryGetValue(call.ArgAt<int>(0), out var row))
					matches[call.ArgAt<int>(0)] = row with { EndedAt = call.ArgAt<DateTime>(1) };
			});
		matchPersistence.FetchMatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => matches.GetValueOrDefault(call.ArgAt<int>(0)));
		matchPersistence.FetchRoundsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Round>>([]));
		matchPersistence.FetchAllMatchesAsync(Arg.Any<CancellationToken>())
			.Returns(_ => (IReadOnlyList<Match>)[.. matches.Values.OrderByDescending(m => m.Id)]);
		matchPersistence.WhenForAnyArgs(m => m.DeleteMatchAsync(default))
			.Do(call => matches.Remove(call.ArgAt<int>(0)));
		matchPersistence.FetchEventsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<MatchEvent>>([]));
		matchPersistence.FetchUnrecoveredMatchesAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Match>>([]));
		matchPersistence.FetchUnrecoveredRoundsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Round>>([]));

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
				services.AddSingleton(matchPersistence);
				services.AddSingleton(TestDoubles.NullUserRepository());
			});
		});
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = null)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	[Theory]
	[InlineData(null)]
	[InlineData("wrong-key")]
	public async Task PostMatch_MissingOrWrongAdminKey_ReturnsUnauthorized(string? adminKey)
	{
		var client = _factory.CreateClient();
		var request = MakeRequest(HttpMethod.Post, "/matches", adminKey);
		request.Content = JsonContent.Create(new { name = "Test" });

		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task PostMatch_ValidAdminKey_CreatesEmptyMatchWithHostZero()
	{
		var client = _factory.CreateClient();
		var request = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
		request.Content = JsonContent.Create(new { name = "Grand Finals" });

		var response = await client.SendAsync(request);

		response.EnsureSuccessStatusCode();
		var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
		var json = envelope.GetProperty("data");
		Assert.Equal("Grand Finals", json.GetProperty("name").GetString());
		Assert.False(json.GetProperty("hasPassword").GetBoolean());
		Assert.False(json.GetProperty("isPrivate").GetBoolean());
		Assert.True(json.GetProperty("host").ValueKind is JsonValueKind.Null);
	}

	[Fact]
	public async Task GetMatch_ListsCreatedMatchByDefault_OnlineStatus()
	{
		var client = _factory.CreateClient();
		var createRequest = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
		createRequest.Content = JsonContent.Create(new { name = "Listed Match" });
		var createResponse = await client.SendAsync(createRequest);
		var createdEnvelope = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
		var id = createdEnvelope.GetProperty("data").GetProperty("id").GetInt32();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/matches"));

		response.EnsureSuccessStatusCode();
		var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
		var items = envelope.GetProperty("data").EnumerateArray().ToList();
		// "online" status means the match is currently live -- reflected by a non-null `live` object
		// (isOpen was dropped in the Phase 2 record redesign; MatchListItem.Live replaces it).
		Assert.Contains(items, item => item.GetProperty("id").GetInt32() == id &&
		                               item.GetProperty("live").ValueKind != JsonValueKind.Null);
	}

	[Fact]
	public async Task PatchSettings_UpdatesNameAndSize()
	{
		var client = _factory.CreateClient();
		var createRequest = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
		createRequest.Content = JsonContent.Create(new { });
		var createResponse = await client.SendAsync(createRequest);
		var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
		var id = created.GetProperty("data").GetProperty("id").GetInt32();

		var patchRequest = MakeRequest(HttpMethod.Patch, $"/matches/{id}/settings", AdminKey);
		patchRequest.Content = JsonContent.Create(new { name = "Renamed", size = 4 });
		var patchResponse = await client.SendAsync(patchRequest);

		patchResponse.EnsureSuccessStatusCode();
		var envelope = await patchResponse.Content.ReadFromJsonAsync<JsonElement>();
		var json = envelope.GetProperty("data");
		Assert.Equal("Renamed", json.GetProperty("name").GetString());
		Assert.Equal(4, json.GetProperty("size").GetInt32());
	}

	[Fact]
	public async Task PatchSettings_UnknownMatchId_ReturnsNotFound()
	{
		var client = _factory.CreateClient();
		var request = MakeRequest(HttpMethod.Patch, "/matches/999999/settings", AdminKey);
		request.Content = JsonContent.Create(new { name = "Nope" });

		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task PostAction_Close_RemovesMatchFromOnlineListing()
	{
		var client = _factory.CreateClient();
		var createRequest = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
		createRequest.Content = JsonContent.Create(new { });
		var createResponse = await client.SendAsync(createRequest);
		var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
		var id = created.GetProperty("data").GetProperty("id").GetInt32();

		var closeRequest = MakeRequest(HttpMethod.Post, $"/matches/{id}/close", AdminKey);
		closeRequest.Content = JsonContent.Create(new { });
		var closeResponse = await client.SendAsync(closeRequest);
		closeResponse.EnsureSuccessStatusCode();

		var listResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/matches"));
		var envelope = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
		var items = envelope.GetProperty("data").EnumerateArray().ToList();
		Assert.DoesNotContain(items, item => item.GetProperty("id").GetInt32() == id);
	}

	/// <summary>
	///     End-to-end smoke test per the route-redesign plan's Verification section: create a match,
	///     open its settings SSE channel, confirm the first event is a full snapshot, confirm a
	///     subsequent settings write produces a delta-only event (not a re-sent full snapshot), and
	///     confirm the raw password is never present in either payload — only `hasPassword`.
	/// </summary>
	[Fact]
	public async Task Smoke_CreateMatch_OpenSettingsSse_FirstEventFull_ThenPatchProducesDeltaWithoutPassword()
	{
		var client = _factory.CreateClient();
		var createRequest = MakeRequest(HttpMethod.Post, "/matches", AdminKey);
		createRequest.Content = JsonContent.Create(new { name = "Smoke Test", password = "hunter2" });
		var createResponse = await client.SendAsync(createRequest);
		var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
		var id = created.GetProperty("data").GetProperty("id").GetInt32();

		var streamClient = _factory.CreateClient();
		var streamRequest = new HttpRequestMessage(HttpMethod.Get, $"/matches/{id}/settings/live")
			{ Headers = { Host = "api.test.local" } };
		streamRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		using var response =
			await streamClient.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
		using var reader = new StreamReader(stream);

		var (firstEvent, firstData) = await ReadNextSseEventAsync(reader, cts.Token);
		Assert.Equal("settings", firstEvent);
		Assert.Contains("\"name\":\"Smoke Test\"", firstData);
		Assert.DoesNotContain("hunter2", firstData);

		var patchRequest = MakeRequest(HttpMethod.Patch, $"/matches/{id}/settings", AdminKey);
		patchRequest.Content = JsonContent.Create(new { name = "Renamed Smoke Test" });
		var patchResponseTask = client.SendAsync(patchRequest, cts.Token);

		var (secondEvent, secondData) = await ReadNextSseEventAsync(reader, cts.Token);
		await patchResponseTask;

		Assert.Equal("settings", secondEvent);
		Assert.Contains("\"name\":\"Renamed Smoke Test\"", secondData);
		// A delta only carries the changed field(s) -- unrelated settings fields from the first
		// event must not be repeated.
		Assert.DoesNotContain("mapId", secondData);
		Assert.DoesNotContain("winCondition", secondData);
		Assert.DoesNotContain("hunter2", secondData);
	}

	[Fact]
	public async Task Smoke_GetUserLive_UserIdZero_IsBlocked()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/users/0/live"));

		// BasilBot (user id 0) has no gameplay stream of its own -- blocked rather than opening a
		// stream that would never receive a frame. Implemented as 400 (not the plan's literal "404"
		// wording) since the id is malformed input for this route, not a missing resource -- same
		// reasoning already applied consistently to every other BasilBot-id guard in UserRoutes.cs.
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	private static async Task<(string? EventType, string Data)> ReadNextSseEventAsync(StreamReader reader,
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
}