using System.Net.Http.Json;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Web;
using Basil.Web.OpenApi;
using Basil.Web.Routing.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `GET /scores`, the new paginated score list. Deliberately not sharing a fixture with
///     <see cref="GetScoresEndpointTests" /> — that file covers the unrelated osu!-client
///     `osu-osz2-getscores.php` endpoint, a naming false-friend for this one.
/// </summary>
public class ScoreListEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	private IReadOnlyList<ScoreRow> _rows = [];

	public ScoreListEndpointTests(WebApplicationFactory<Program> factory)
	{
		var scores = Substitute.For<IScoreRepository>();
		scores.FetchCountAsync(Arg.Any<CancellationToken>()).Returns(_ => _rows.Count);
		scores.FetchPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => (IReadOnlyList<ScoreRow>)
				[.. _rows.Skip(call.ArgAt<int>(0)).Take(call.ArgAt<int>(1))]);
		scores.FetchByRoundAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<ScoreReport>>([]));

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
				services.AddSingleton(scores);
				services.AddSingleton(TestDoubles.NullUserRepository());
				services.AddSingleton(TestDoubles.NullMapRepository());
			});
		});
	}

	private static HttpRequestMessage MakeRequest(string path)
	{
		return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
	}

	private static ScoreRow MakeRow(long id)
	{
		return new ScoreRow(
			id, null, null, new string('a', 32), 900_000, 98.5, 500, Mods.NoMod,
			300, 10, 5, 0, 0, 0, "S", GameMode.Standard, DateTime.UtcNow, 120_000,
			ClientFlags.Clean, 7, false, $"checksum-{id}", DateTime.UtcNow);
	}

	[Fact]
	public async Task GetScores_NoRows_ReturnsEmptyPage()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest("/scores"));
		var body = await response.Content.ReadFromJsonAsync<Envelope<List<ScoreListItem>>>();

		response.EnsureSuccessStatusCode();
		Assert.NotNull(body);
		Assert.NotNull(body.Meta);
		Assert.Equal(1, body.Meta!.Page);
		Assert.Equal(Pagination.DefaultPageSize, body.Meta.PageSize);
		Assert.Empty(body.Data!);
		Assert.Equal(0, body.Meta.TotalRecords);
	}

	[Fact]
	public async Task GetScores_FewerThanPageSize_ReturnsAllRows()
	{
		_rows = [MakeRow(3), MakeRow(2), MakeRow(1)];

		var response = await _factory.CreateClient().SendAsync(MakeRequest("/scores"));
		var body = await response.Content.ReadFromJsonAsync<Envelope<List<ScoreListItem>>>();

		response.EnsureSuccessStatusCode();
		Assert.Equal(3, body!.Meta!.TotalRecords);
		Assert.Equal(1, body.Meta.TotalPages);
		Assert.Equal([3L, 2L, 1L], body.Data!.Select(r => r.Id));
	}

	[Fact]
	public async Task GetScores_PageSizeSmallerThanRows_ReportsMultiplePages()
	{
		_rows = [MakeRow(3), MakeRow(2), MakeRow(1)];

		var response = await _factory.CreateClient().SendAsync(MakeRequest("/scores?page=1&pageSize=2"));
		var body = await response.Content.ReadFromJsonAsync<Envelope<List<ScoreListItem>>>();

		response.EnsureSuccessStatusCode();
		Assert.Equal(3, body!.Meta!.TotalRecords);
		Assert.Equal(2, body.Meta.TotalPages);
		Assert.Equal([3L, 2L], body.Data!.Select(r => r.Id));
	}

	/// <summary>
	///     Minimal local shape for `GET /scores` list items — the real <c>ScoreDetailView</c>
	///     is an internal nested type of <c>ScoreRoutes</c>, not accessible from this test project.
	/// </summary>
	private sealed record ScoreListItem(long Id);
}