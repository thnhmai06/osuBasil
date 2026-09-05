using System.Net;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Media;
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
///     Covers the single-flight guard around the `b.` host's audio-preview route: concurrent
///     requests for the same beatmapset's preview, all racing a cold cache, must trigger at most one
///     ffmpeg extraction rather than one process per concurrent request.
/// </summary>
public class AudioPreviewSingleFlightTests(WebApplicationFactory<Program> factory)
	: IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-preview-tests-").FullName;

	public void Dispose()
	{
		if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
	}

	/// <summary>Counts and delays extraction calls, long enough for concurrent requests below to queue behind the single-flight lock rather than racing past it before the first call starts.</summary>
	private sealed class CountingAudioExtractor : IAudioExtractor
	{
		public int CallCount;

		public async Task<byte[]> ExtractAsync(string audioFilePath, int startMs, TimeSpan duration,
			CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref CallCount);
			await Task.Delay(200, cancellationToken);
			return "clip"u8.ToArray();
		}
	}

	[Fact]
	public async Task Preview_ConcurrentRequests_ExtractsOnlyOnce()
	{
		var beatmapset = new Beatmapset(901, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		var beatmap = new Beatmap(new string('a', 32), 1, beatmapset, "Normal", "diff.osu",
			new Difficulty(GameMode.Standard, 180, TimeSpan.FromSeconds(100), 4, 9, 8, 5, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 }, AudioFile: "audio.mp3");

		var beatmapsets = Substitute.For<IBeatmapsetRepository>();
		beatmapsets.FetchByIdAsync(901, Arg.Any<CancellationToken>()).Returns(beatmapset);
		var maps = Substitute.For<IBeatmapRepository>();
		maps.FetchAllBySetIdAsync(901, Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Beatmap>>([beatmap]));

		var folder = Path.Combine(_dataDir, "Beatmapsets", "901 Artist - Title");
		Directory.CreateDirectory(folder);
		await File.WriteAllBytesAsync(Path.Combine(folder, "audio.mp3"), [1, 2, 3]);

		var extractor = new CountingAudioExtractor();

		var client = factory.WithWebHostBuilder(builder =>
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
				services.AddSingleton(beatmapsets);
				services.AddSingleton(maps);
				services.AddSingleton<IAudioExtractor>(extractor);
				services.AddSingleton(Options.Create(new StorageOptions
				{
					ReplaysPath = Path.Combine(_dataDir, "Replays"),
					AvatarsPath = Path.Combine(_dataDir, "Avatars"),
					BeatmapsetsPath = Path.Combine(_dataDir, "Beatmapsets"),
					MenuSeasonalsPath = Path.Combine(_dataDir, "Seasonals"),
					MenuBannersPath = Path.Combine(_dataDir, "Banners"),
					FaqsPath = Path.Combine(_dataDir, "Faqs"),
					CachePath = Path.Combine(_dataDir, "Cache")
				}));
			});
		}).CreateClient();

		Task<HttpResponseMessage> MakeRequest()
		{
			return client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/preview/901.mp3")
				{ Headers = { Host = "b.test.local" } });
		}

		var responses = await Task.WhenAll(MakeRequest(), MakeRequest(), MakeRequest(), MakeRequest(), MakeRequest());

		foreach (var r in responses)
		{
			Assert.Equal(HttpStatusCode.OK, r.StatusCode);
			Assert.Equal("clip"u8.ToArray(), await r.Content.ReadAsByteArrayAsync());
		}

		Assert.Equal(1, extractor.CallCount);
	}
}