using System.Threading.Channels;
using Basil.Application.Events;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Beatmaps;

/// <summary>Ingests a beatmapset archive: analyzes each difficulty and stores the metadata and the archive.</summary>
public sealed class BeatmapCatalog(
	IBeatmapAnalyser calculator,
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	IBeatmapArchiveStorage archives,
	TimeProvider time) : IEventPublisher<BeatmapsetEvent>
{
	private readonly Channel<BeatmapsetEvent> _events = Channel.CreateUnbounded<BeatmapsetEvent>();

	/// <inheritdoc />
	public ChannelReader<BeatmapsetEvent> Events => _events.Reader;

	/// <summary>
	///     Imports a beatmapset: stores its archive, its metadata and each analyzed difficulty, and drops difficulties
	///     the new version no longer has.
	/// </summary>
	/// <param name="beatmapsetId">The id to store the set under.</param>
	/// <param name="artist">The set's artist.</param>
	/// <param name="title">The set's title.</param>
	/// <param name="creator">The set's creator.</param>
	/// <param name="difficultyFiles">Each difficulty's id, version name, .osu file bytes and game mode.</param>
	/// <param name="archiveContent">The complete .osz archive bytes.</param>
	/// <param name="cancellationToken">A token that cancels the import.</param>
	/// <returns>The imported beatmapset and its analyzed difficulties, or <see langword="null" /> when the set is frozen.</returns>
	public async Task<(Beatmapset Set, IReadOnlyList<Beatmap> Beatmaps)?> ImportAsync(
		int beatmapsetId, string artist, string title, User creator,
		IReadOnlyList<(int Id, string Version, byte[] Content, GameMode Mode)> difficultyFiles,
		byte[] archiveContent, CancellationToken cancellationToken = default)
	{
		var existing = await beatmapsets.GetAsync(beatmapsetId, cancellationToken);
		if (existing is { Locked: true }) return null;

		var now = time.GetUtcNow();
		var set = new Beatmapset
		{
			Id = beatmapsetId, Artist = artist, Title = title, Creator = creator, UpdatedAt = now,
			CreatedAt = existing?.CreatedAt ?? now, Visible = existing?.Visible ?? true
		};

		await using var content = new MemoryStream(archiveContent, false);
		await archives.SaveAsync(set, content, cancellationToken);

		await beatmapsets.CreateOrUpdateAsync(set, cancellationToken);

		var result = new List<Beatmap>(difficultyFiles.Count);
		foreach (var file in difficultyFiles)
		{
			var analysis = calculator.Analyze(file.Content, file.Mode, GameMods.NoMod);
			var beatmap = new Beatmap
			{
				Id = file.Id,
				Hash = new Md5(file.Content),
				Beatmapset = set,
				Version = file.Version,
				Difficulty = analysis.Difficulty,
				Objects = analysis.Objects
			};
			await beatmaps.CreateOrUpdateAsync(beatmap, cancellationToken);
			result.Add(beatmap);
		}

		await beatmaps.RetainAsync(set, result, cancellationToken);

		_events.Writer.TryWrite(new BeatmapsetImported(set, result));

		return (set, result);
	}
}