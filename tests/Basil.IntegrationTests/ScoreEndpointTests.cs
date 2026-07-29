using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Web;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the new public `/scores` routes: `GET /scores/{scoreId}` (a score's full row, new — a score
///     used to only be visible embedded in a match report) and `GET /scores/{scoreId}/replay` (a direct
///     rename of the old bare `GET /replays/{scoreId}`).
/// </summary>
public class ScoreEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	private ScoreOwnerRow? _owner;
	private byte[]? _replayBytes;
	private ScoreRow? _row;

	public ScoreEndpointTests(WebApplicationFactory<Program> factory)
	{
		var scores = Substitute.For<IScoreRepository>();
		scores.FetchCountAsync(Arg.Any<CancellationToken>()).Returns(_ => _row is null ? 0 : 1);
		scores.FetchOwnerAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => _owner);
		scores.FetchByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => _row);
		scores.FetchPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<ScoreRow>>([]));
		scores.FetchByRoundIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<RoundScoreRow>>([]));

		var replayStorage = Substitute.For<IReplayStorage>();
		replayStorage.ReadAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => _replayBytes);

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
				services.AddSingleton(scores);
				services.AddSingleton(replayStorage);
				services.AddSingleton(TestDoubles.NullUserRepository());
				services.AddSingleton(TestDoubles.NullMapRepository());
			});
		});
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path)
	{
		return new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
	}

	[Fact]
	public async Task GetScore_UnknownId_ReturnsNotFound()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/999"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task GetScore_KnownId_ReturnsFullRow()
	{
		_row = new ScoreRow(
			42, null, null, new string('a', 32), 900_000, 98.5, 500, Mods.Hidden,
			300, 10, 5, 0, 0, 0, "S", GameMode.Standard, DateTime.UtcNow, 120_000,
			ClientFlags.Clean, 7, false, "checksum", DateTime.UtcNow);

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42"));
		var envelope = await response.Content.ReadFromJsonAsync<Envelope<ScoreShape>>();

		response.EnsureSuccessStatusCode();
		var body = envelope!.Data;
		Assert.NotNull(body);
		Assert.Equal(42, body.Id);
		Assert.Equal(900_000, body.TotalScore);
		Assert.Equal(7, body.User.Id);
	}

	[Fact]
	public async Task GetReplay_UnknownScore_ReturnsNotFound()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/999/replay"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task GetReplay_KnownScoreNoStoredReplay_ReturnsNotFound()
	{
		_owner = new ScoreOwnerRow(7, GameMode.Standard);

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42/replay"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task GetReplay_Found_ReturnsReplayBytes()
	{
		_owner = new ScoreOwnerRow(7, GameMode.Standard);
		_replayBytes = [1, 2, 3, 4];

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/scores/42/replay"));
		var bytes = await response.Content.ReadAsByteArrayAsync();

		response.EnsureSuccessStatusCode();
		Assert.Equal("application/x-osu-replay", response.Content.Headers.ContentType?.MediaType);
		Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes);
	}

	private sealed record ScoreShape(long Id, long TotalScore, UserBriefShape User);

	private sealed record UserBriefShape(int Id, string Name, string Country);
}