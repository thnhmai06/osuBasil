using System.Net.Http.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configurations;
using Basil.Application.Services.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Web;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `GET /beatmapsets`, the paginated beatmapset list. This is the N+1 fix (Phase 4 of
///     the 2026 perf investigation): the route used to call
///     <see cref="IBeatmapRepository.FetchAllBySetIdAsync" /> once per page item just to read its
///     `.Count`, one query per row on top of the page query itself. It now batches every page
///     item's count into a single <see cref="IBeatmapRepository.FetchCountsBySetIdsAsync" /> call.
/// </summary>
public class BeatmapsetListEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	private readonly IBeatmapRepository _maps = Substitute.For<IBeatmapRepository>();
	private IReadOnlyList<Beatmapset> _sets = [];

	public BeatmapsetListEndpointTests(WebApplicationFactory<Program> factory)
	{
		var mapsets = Substitute.For<IBeatmapsetRepository>();
		mapsets.FetchCountAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ => _sets.Count);
		mapsets.FetchPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(call => (IReadOnlyList<Beatmapset>)
				[.. _sets.Skip(call.ArgAt<int>(0)).Take(call.ArgAt<int>(1))]);

		_maps.FetchCountsBySetIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<bool>(),
				Arg.Any<CancellationToken>())
			.Returns(call => (IReadOnlyDictionary<int, int>)call.ArgAt<IReadOnlyCollection<int>>(0)
				.ToDictionary(id => id, id => id * 10));

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
				services.AddSingleton(mapsets);
				services.AddSingleton(_maps);
			});
		});
	}

	private static HttpRequestMessage MakeRequest(string path)
	{
		return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
	}

	private static Beatmapset MakeMapset(int id)
	{
		return new Beatmapset(id, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
	}

	[Fact]
	public async Task GetBeatmapsets_BatchesCountsInsteadOfOnePerItem()
	{
		_sets = [MakeMapset(1), MakeMapset(2), MakeMapset(3)];

		var response = await _factory.CreateClient().SendAsync(MakeRequest("/beatmapsets"));
		var body = await response.Content.ReadFromJsonAsync<Envelope<List<BeatmapsetSummary>>>();

		response.EnsureSuccessStatusCode();
		Assert.Equal([10, 20, 30], body!.Data!.Select(s => s.BeatmapCount));
		await _maps.Received(1).FetchCountsBySetIdsAsync(
			Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
		await _maps.DidNotReceive().FetchAllBySetIdAsync(
			Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
	}
}