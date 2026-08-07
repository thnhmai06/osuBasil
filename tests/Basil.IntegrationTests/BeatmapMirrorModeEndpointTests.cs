using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the online-mirror-mode fallback: local storage is always tried first (see
///     <see cref="BeatmapRedirectEndpointTests" /> for `/d/{id}`'s local-first coverage), and only a
///     beatmapset genuinely missing locally is affected by <see cref="MirrorOptions.IsOnlineMode" />
///     — a redirect for a genuine ppy id, `503` for a locally-authored (synthesized-id) set or a
///     route with no mirror equivalent, `404` when offline.
/// </summary>
public class BeatmapMirrorModeEndpointTests(WebApplicationFactory<Program> factory)
	: IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-mirror-tests-").FullName;
	private Beatmapset? _mapset;

	public void Dispose()
	{
		if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
	}

	private WebApplicationFactory<Program> Configure(string? downloadEndpoint)
	{
		var mapsets = Substitute.For<IBeatmapsetRepository>();
		mapsets.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => _mapset?.Id == call.ArgAt<int>(0) ? _mapset : null);
		var maps = Substitute.For<IBeatmapRepository>();
		maps.FetchAllBySetIdAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Beatmap>>([]));

		return factory.WithWebHostBuilder(builder =>
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
				services.AddSingleton(maps);
				if (downloadEndpoint is not null)
					services.AddSingleton(Options.Create(new MirrorOptions { DownloadEndpoint = downloadEndpoint }));
				services.AddSingleton(Options.Create(new StorageOptions
				{
					ReplaysPath = Path.Combine(_dataDir, "Replays"),
					AvatarsPath = Path.Combine(_dataDir, "Avatars"),
					MapsetsPath = Path.Combine(_dataDir, "Mapsets"),
					SeasonalsPath = Path.Combine(_dataDir, "Seasonals"),
					FaqsPath = Path.Combine(_dataDir, "Faqs"),
					CachePath = Path.Combine(_dataDir, "Cache")
				}));
			});
		});
	}

	private static HttpRequestMessage MakeRequest(string path, string host = "b.test.local")
	{
		return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = host } };
	}

	// ---- b. host thumb/preview ----

	[Fact]
	public async Task Thumb_OfflineMode_MissingLocal_ReturnsNotFound()
	{
		_mapset = new Beatmapset(555, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		var client = Configure(null).CreateClient(new WebApplicationFactoryClientOptions
			{ AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest("/thumb/555.jpg"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task Thumb_OnlineMode_GenuinePpyId_MissingLocal_RedirectsToBPpySh()
	{
		_mapset = new Beatmapset(555, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		var client = Configure("https://mirror.local/d").CreateClient(new WebApplicationFactoryClientOptions
			{ AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest("/thumb/555.jpg"));

		Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
		Assert.Equal("https://b.ppy.sh/thumb/555.jpg", response.Headers.Location!.ToString());
	}

	[Fact]
	public async Task Thumb_OnlineMode_LocallySynthesizedId_MissingLocal_ReturnsServiceUnavailable()
	{
		_mapset = new Beatmapset(Beatmap.LocalIdFloor, "Artist", "Title", "Creator", DateTime.UtcNow,
			DateTime.UtcNow);
		var client = Configure("https://mirror.local/d").CreateClient(new WebApplicationFactoryClientOptions
			{ AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest($"/thumb/{Beatmap.LocalIdFloor}.jpg"));

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
	}

	[Fact]
	public async Task Thumb_OnlineMode_LocalFileExists_ServesLocalIgnoringMode()
	{
		var mapsetId = 556;
		_mapset = new Beatmapset(mapsetId, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow)
			{ BackgroundFile = "bg.png" };
		var folder = Path.Combine(_dataDir, "Mapsets", $"{mapsetId} Artist - Title");
		Directory.CreateDirectory(folder);
		await File.WriteAllBytesAsync(Path.Combine(folder, "bg.png"),
			Convert.FromBase64String(
				"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

		var client = Configure("https://mirror.local/d").CreateClient(new WebApplicationFactoryClientOptions
			{ AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest($"/thumb/{mapsetId}.jpg"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	// ---- api. host local-only asset routes (no mirror equivalent) ----

	[Fact]
	public async Task ApiBackground_OnlineMode_MissingLocal_ReturnsServiceUnavailable()
	{
		_mapset = new Beatmapset(700, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		var client = Configure("https://mirror.local/d").CreateClient();

		var response = await client.SendAsync(MakeRequest("/beatmapsets/700/background", "api.test.local"));

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
	}

	[Fact]
	public async Task ApiBackground_OfflineMode_MissingLocal_ReturnsNotFound_Unchanged()
	{
		_mapset = new Beatmapset(701, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		var client = Configure(null).CreateClient();

		var response = await client.SendAsync(MakeRequest("/beatmapsets/701/background", "api.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	// ---- api. host /beatmapsets/{id}/download (has a mirror equivalent) ----

	[Fact]
	public async Task ApiDownload_OnlineMode_GenuinePpyId_MissingLocal_RedirectsToMirror()
	{
		_mapset = new Beatmapset(800, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		var client = Configure("https://mirror.local/d").CreateClient(new WebApplicationFactoryClientOptions
			{ AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest("/beatmapsets/800/download", "api.test.local"));

		Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
		Assert.Equal("https://mirror.local/d/800?n=1", response.Headers.Location!.ToString());
	}

	[Fact]
	public async Task ApiDownload_OnlineMode_LocallySynthesizedId_MissingLocal_ReturnsServiceUnavailable()
	{
		_mapset = new Beatmapset(Beatmap.LocalIdFloor, "Artist", "Title", "Creator", DateTime.UtcNow,
			DateTime.UtcNow);
		var client = Configure("https://mirror.local/d").CreateClient();

		var response = await client.SendAsync(
			MakeRequest($"/beatmapsets/{Beatmap.LocalIdFloor}/download", "api.test.local"));

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
	}

	[Fact]
	public async Task ApiDownload_OfflineMode_MissingLocal_ReturnsNotFound_Unchanged()
	{
		_mapset = new Beatmapset(801, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		var client = Configure(null).CreateClient();

		var response = await client.SendAsync(MakeRequest("/beatmapsets/801/download", "api.test.local"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task ApiDownload_OnlineMode_UnknownId_GenuinePpyIdSpace_RedirectsToMirror()
	{
		_mapset = null; // Basil has never ingested this set — the mirror-search-discovered case.
		var client = Configure("https://mirror.local/d").CreateClient(new WebApplicationFactoryClientOptions
			{ AllowAutoRedirect = false });

		var response = await client.SendAsync(MakeRequest("/beatmapsets/900/download", "api.test.local"));

		Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
		Assert.Equal("https://mirror.local/d/900?n=1", response.Headers.Location!.ToString());
	}
}