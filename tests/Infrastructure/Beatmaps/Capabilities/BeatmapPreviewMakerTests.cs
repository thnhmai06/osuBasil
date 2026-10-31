using Basil.Testing.Persistence;
using System.Text;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps.Capabilities;
using Basil.Infrastructure.Beatmaps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Infrastructure.Beatmaps.Tests.Capabilities;

/// <summary>Pins which audio tracks get a preview, and that a failure leaves the rest of the set alone.</summary>
public sealed class BeatmapPreviewMakerTests : IDisposable
{
	private readonly BeatmapPreviewMaker _assets;
	private readonly string _cache = Path.Combine(Path.GetTempPath(), "basil-assets-" + Guid.NewGuid().ToString("N"));
	private readonly List<(string Name, byte[] Content)> _previews = [];
	private readonly IBeatmapsetStorage _storage = Substitute.For<IBeatmapsetStorage>();

	/// <summary>Composes the capability over a storage the test controls and a temporary cache directory.</summary>
	public BeatmapPreviewMakerTests()
	{
		_storage.SaveAsync(Arg.Any<BeatmapsetRecord>(), BeatmapsetFileType.Preview, Arg.Any<string>(),
				Arg.Any<Stream>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				using var content = new MemoryStream();
				call.ArgAt<Stream>(3).CopyTo(content);
				_previews.Add((call.ArgAt<string>(2), content.ToArray()));
				return Task.CompletedTask;
			});
		_assets = new BeatmapPreviewMaker(Options.Create(new AssetOptions { CacheDirectory = _cache }), _storage,
			NullLogger<BeatmapPreviewMaker>.Instance);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (Directory.Exists(_cache)) Directory.Delete(_cache, true);
	}

	/// <summary>An audio track the set has gets a preview named after it, and the temporary files are cleaned up.</summary>
	[Fact]
	public async Task BuildAsync_MakesAPreviewOfTheAudioTheDifficultyUses()
	{
		Assert.SkipWhen(!FfmpegIsInstalled(), "ffmpeg and ffprobe are not installed.");
		var set = AssetBeatmaps.Set();
		var beatmap = AssetBeatmaps.Beatmap(set, "audio.mp3", TimeSpan.FromSeconds(30));
		GiveAudio("audio.mp3", OszArchive.Sample(Path.Combine("Audio", "preview-source.mp3")));

		await _assets.BuildAsync(set, [beatmap], TestContext.Current.CancellationToken);

		var preview = Assert.Single(_previews);
		Assert.Equal("audio.mp3", preview.Name);
		Assert.NotEmpty(preview.Content);
		Assert.Empty(Directory.EnumerateFiles(_cache));
	}

	/// <summary>An Ogg Vorbis track gets a preview like an mp3 does.</summary>
	[Fact]
	public async Task BuildAsync_MakesAPreviewOfAnOggTrack()
	{
		Assert.SkipWhen(!FfmpegIsInstalled(), "ffmpeg and ffprobe are not installed.");
		var set = AssetBeatmaps.Set();
		var beatmap = AssetBeatmaps.Beatmap(set, "audio.ogg", TimeSpan.Zero);
		GiveAudio("audio.ogg", OszArchive.Sample(Path.Combine("Audio", "hitsound.ogg")));

		await _assets.BuildAsync(set, [beatmap], TestContext.Current.CancellationToken);

		var preview = Assert.Single(_previews);
		Assert.Equal("audio.ogg", preview.Name);
		Assert.NotEmpty(preview.Content);
	}

	/// <summary>Every distinct audio track gets one preview, however many difficulties use it.</summary>
	[Fact]
	public async Task BuildAsync_MakesOnePreviewPerAudioTrack()
	{
		Assert.SkipWhen(!FfmpegIsInstalled(), "ffmpeg and ffprobe are not installed.");
		var set = AssetBeatmaps.Set();
		var first = AssetBeatmaps.Beatmap(set);
		var second = AssetBeatmaps.Beatmap(set);
		GiveAudio("audio.mp3", OszArchive.Sample(Path.Combine("Audio", "preview-source.mp3")));

		await _assets.BuildAsync(set, [first, second], TestContext.Current.CancellationToken);

		Assert.Single(_previews);
	}

	/// <summary>An audio track the set does not have makes no preview and fails nothing.</summary>
	[Fact]
	public async Task BuildAsync_SkipsAnAudioTheSetDoesNotHave()
	{
		var set = AssetBeatmaps.Set();
		var beatmap = AssetBeatmaps.Beatmap(set);

		await _assets.BuildAsync(set, [beatmap], TestContext.Current.CancellationToken);

		Assert.Empty(_previews);
		Assert.False(Directory.Exists(_cache) && Directory.EnumerateFiles(_cache).Any());
	}

	/// <summary>Content that is no audio makes no preview and fails nothing else in the set.</summary>
	[Fact]
	public async Task BuildAsync_SurvivesAnAudioThatIsNoAudio()
	{
		var set = AssetBeatmaps.Set();
		var beatmap = AssetBeatmaps.Beatmap(set, "broken.mp3");
		GiveAudio("broken.mp3", Encoding.UTF8.GetBytes("this is not audio"));

		await _assets.BuildAsync(set, [beatmap], TestContext.Current.CancellationToken);

		Assert.Empty(_previews);
		Assert.False(Directory.Exists(_cache) && Directory.EnumerateFiles(_cache).Any());
	}

	/// <summary>A difficulty that names no audio track has no preview to make.</summary>
	[Fact]
	public async Task BuildAsync_IgnoresADifficultyWithoutAudio()
	{
		var set = AssetBeatmaps.Set();
		var beatmap = AssetBeatmaps.Beatmap(set, null);

		await _assets.BuildAsync(set, [beatmap], TestContext.Current.CancellationToken);

		Assert.Empty(_previews);
		await _storage.DidNotReceive().OpenAsync(Arg.Any<BeatmapsetRecord>(), Arg.Any<BeatmapsetFileType>(),
			Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	/// <summary>Makes the storage give back the given content under a name.</summary>
	/// <param name="name">The stored audio's name.</param>
	/// <param name="content">The stored audio's content.</param>
	private void GiveAudio(string name, byte[] content)
	{
		_storage.OpenAsync(Arg.Any<BeatmapsetRecord>(), BeatmapsetFileType.Audio, name, Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult<Stream?>(new MemoryStream(content, false)));
	}

	/// <summary>Whether the ffmpeg tools the capability calls are on the PATH.</summary>
	/// <returns><see langword="true" /> when both tools are found.</returns>
	private static bool FfmpegIsInstalled()
	{
		var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
		return new[] { "ffmpeg", "ffprobe" }.All(tool => directories.Any(directory =>
			File.Exists(Path.Combine(directory, OperatingSystem.IsWindows() ? tool + ".exe" : tool))));
	}
}