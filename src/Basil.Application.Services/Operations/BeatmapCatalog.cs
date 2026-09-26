using Basil.Application.Contracts.Ports;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Contracts.Storages;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations;

/// <summary>Ingests a beatmapset archive: analyzes each difficulty and stores the metadata and the archive.</summary>
public sealed class BeatmapCatalog(
	IOsuCalculator calculator,
	IRepository<int, Beatmapset> beatmapsets,
	IRepository<int, Beatmap> beatmaps,
	IBlobStorage<int> archives)
{
	/// <summary>Imports a beatmapset: stores its metadata, analyzes each difficulty, and stores the archive.</summary>
	/// <param name="beatmapsetId">The id to store the set under.</param>
	/// <param name="artist">The set's artist.</param>
	/// <param name="title">The set's title.</param>
	/// <param name="creator">The set's creator.</param>
	/// <param name="difficultyFiles">Each difficulty's id, version name, decoded .osu file path, and raw bytes.</param>
	/// <param name="archiveContent">The complete .osz archive bytes.</param>
	/// <param name="cancellationToken">A token that cancels the import.</param>
	/// <returns>The imported beatmapset and its analyzed difficulties, or <see langword="null" /> when the set is frozen.</returns>
	public async Task<(Beatmapset Set, IReadOnlyList<Beatmap> Beatmaps)?> ImportAsync(
		int beatmapsetId, string artist, string title, User creator,
		IReadOnlyList<(int Id, string Version, string FilePath, byte[] Content, GameMode Mode)> difficultyFiles,
		byte[] archiveContent, CancellationToken cancellationToken = default)
	{
		var existing = await beatmapsets.LoadAsync(beatmapsetId, cancellationToken);
		if (existing is { Locked: true }) return null;

		var now = DateTimeOffset.UtcNow;
		var set = new Beatmapset
		{
			Id = beatmapsetId, Artist = artist, Title = title, Creator = creator, LastUpdate = now,
			CreatedAt = existing?.CreatedAt ?? now, Visible = existing?.Visible ?? true
		};
		await beatmapsets.SaveAsync(set, cancellationToken);

		var result = new List<Beatmap>(difficultyFiles.Count);
		foreach (var file in difficultyFiles)
		{
			var analysis = calculator.Analyze(file.FilePath, file.Mode, GameMods.NoMod);
			var beatmap = new Beatmap
			{
				Id = file.Id,
				Md5 = calculator.ComputeBeatmapMd5(file.Content),
				Beatmapset = set,
				Version = file.Version,
				Difficulty = analysis.Difficulty,
				Objects = analysis.Objects
			};
			await beatmaps.SaveAsync(beatmap, cancellationToken);
			result.Add(beatmap);
		}

		await using var content = new MemoryStream(archiveContent, writable: false);
		await archives.SaveAsync(beatmapsetId, content, cancellationToken);

		return (set, result);
	}
}