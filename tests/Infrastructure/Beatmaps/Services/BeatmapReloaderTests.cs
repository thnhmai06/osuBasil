using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Beatmaps.Services;
using Basil.Infrastructure.Beatmaps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Basil.Infrastructure.Beatmaps.Tests.Services;

/// <summary>Recomputing the stored metadata of every beatmap with the rulesets in use.</summary>
public sealed class BeatmapReloaderTests
{
	private const double NewStar = 7.25;
	private static readonly CancellationToken Token = TestContext.Current.CancellationToken;
	private readonly IBeatmapAnalyser _analyser = Substitute.For<IBeatmapAnalyser>();

	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly IBeatmapsetRepository _sets = Substitute.For<IBeatmapsetRepository>();
	private readonly IBeatmapsetStorage _storage = Substitute.For<IBeatmapsetStorage>();

	private BeatmapReloader NewReloader()
	{
		return new BeatmapReloader(_sets, _beatmaps, _storage, _analyser,
			NullLogger<BeatmapReloader>.Instance);
	}

	/// <summary>Serves the given sets on the first page and nothing after it.</summary>
	private BeatmapRecord OneSetWithABeatmap()
	{
		var set = AssetBeatmaps.Set();
		var beatmap = AssetBeatmaps.Beatmap(set);
		_sets.ListAsync(Arg.Any<BeatmapQuery>(), Arg.Is<PageRequest>(page => page.Offset == 0),
				Arg.Any<CancellationToken>())
			.Returns(new Page<BeatmapsetRecord>([set], false));
		_sets.ListAsync(Arg.Any<BeatmapQuery>(), Arg.Is<PageRequest>(page => page.Offset > 0),
				Arg.Any<CancellationToken>())
			.Returns(new Page<BeatmapsetRecord>([], false));
		_beatmaps.ListAsync(set, Arg.Any<CancellationToken>()).Returns([beatmap]);
		return beatmap;
	}

	[Fact]
	public async Task Reload_StoresStarRating()
	{
		var beatmap = OneSetWithABeatmap();
		_storage.OpenAsync(beatmap.Beatmapset, BeatmapsetFileType.Version, $"{beatmap.Hash}.osu",
				Arg.Any<CancellationToken>())
			.Returns(new MemoryStream([1]));
		var difficulty = beatmap.Metadata.Difficulty with { Star = NewStar };
		_analyser.Analyze(Arg.Any<Stream>(), GameMode.Standard, GameMods.NoMod)
			.Returns(new BeatmapAnalysis(difficulty, beatmap.Metadata.Objects));

		var (done, failed) = await NewReloader().ReloadAllAsync(Token);

		Assert.Equal((1, 0), (done, failed));
		await _beatmaps.Received(1).UpdateAsync(
			Arg.Is<BeatmapRecord>(stored => stored.Metadata.Difficulty.Star == NewStar));
	}

	[Fact]
	public async Task MissingFile_KeepsValues()
	{
		var beatmap = OneSetWithABeatmap();
		_storage.OpenAsync(beatmap.Beatmapset, BeatmapsetFileType.Version, $"{beatmap.Hash}.osu",
				Arg.Any<CancellationToken>())
			.Returns((Stream?)null);

		var (done, failed) = await NewReloader().ReloadAllAsync(Token);

		Assert.Equal((0, 1), (done, failed));
		await _beatmaps.DidNotReceive().UpdateAsync(Arg.Any<BeatmapRecord>());
	}

	[Fact]
	public async Task Unanalysable_KeepsValues()
	{
		var beatmap = OneSetWithABeatmap();
		_storage.OpenAsync(beatmap.Beatmapset, BeatmapsetFileType.Version, $"{beatmap.Hash}.osu",
				Arg.Any<CancellationToken>())
			.Returns(new MemoryStream([1]));
		_analyser.Analyze(Arg.Any<Stream>(), Arg.Any<GameMode>(), Arg.Any<GameMods>())
			.Returns(_ => throw new InvalidDataException());

		var (done, failed) = await NewReloader().ReloadAllAsync(Token);

		Assert.Equal((0, 1), (done, failed));
		await _beatmaps.DidNotReceive().UpdateAsync(Arg.Any<BeatmapRecord>());
	}

	[Fact]
	public async Task TryStartRunning_Refused()
	{
		var release = new TaskCompletionSource<Page<BeatmapsetRecord>>();
		_sets.ListAsync(Arg.Any<BeatmapQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
			.Returns(_ => release.Task);
		var reloader = NewReloader();

		var first = reloader.TryStart();
		var second = reloader.TryStart();
		release.SetResult(new Page<BeatmapsetRecord>([], false));

		Assert.True(first);
		Assert.False(second);
	}
}