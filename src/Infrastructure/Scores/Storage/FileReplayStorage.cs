using System.Buffers;
using System.Globalization;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Storage;
using Basil.Application.Users.Persistence;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Common.Storage;

namespace Basil.Infrastructure.Scores.Storage;

/// <summary>Stores the raw .osr bytes of scores' replays, one file per score, named like osu! names an exported replay.</summary>
internal sealed class FileReplayStorage(
	DataPaths paths,
	IUserRepository users,
	IBeatmapRepository beatmaps) : IReplayStorage
{
	/// <summary>The most bytes a replay's file name may take: the 255 of common file systems, less the extension.</summary>
	private const int MaxNameBytes = 255 - 4;

	private static readonly SearchValues<char> UnportableChars = SearchValues.Create("<>:\"/\\|?*");
	private DataStorage Files => paths.Replays;

	/// <inheritdoc />
	public async Task SaveAsync(ScoreRecord score, Stream content, CancellationToken cancellationToken = default)
	{
		var name = await NameOfAsync(score, cancellationToken);
		await Files.SaveAsync(name, content, cancellationToken);
		foreach (var previous in Find(score.Id).Where(previous => previous != name).ToList())
			Files.Delete(previous);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(ScoreRecord score, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Find(score.Id).FirstOrDefault() is { } name ? Files.Open(name) : null);
	}

	private IEnumerable<string> Find(int id)
	{
		var prefix = id.ToString(CultureInfo.InvariantCulture);
		return Files.Search(prefix + "*.osr").Where(name =>
			name.EndsWith(".osr", StringComparison.Ordinal) &&
			name.Length > prefix.Length &&
			name[prefix.Length] is ' ' or '.' &&
			name.StartsWith(prefix, StringComparison.Ordinal));
	}

	private async Task<string> NameOfAsync(ScoreRecord score, CancellationToken cancellationToken)
	{
		var user = score.Value.UserId is { } userId ? await users.GetAsync(userId, cancellationToken) : null;
		var beatmap = score.Value.BeatmapHash is { } hash ? await beatmaps.GetAsync(hash, cancellationToken) : null;
		if (user is null || beatmap is null)
			return $"{score.Id}.osr";

		var name = string.Format(CultureInfo.InvariantCulture,
			"{0} {1} - {2} ({3:yyyy-MM-dd}) {4}",
			score.Id, user.Value.Name, beatmap.Name, score.Value.Timestamp.UtcDateTime,
			ModeName(score.Value.Mode));
		return FileNames.Fitted(Portable(name), MaxNameBytes) + ".osr";
	}

	private static string ModeName(GameMode mode)
	{
		return mode switch
		{
			GameMode.Standard => "Osu",
			GameMode.Taiko => "Taiko",
			GameMode.Catch => "CatchTheBeat",
			GameMode.Mania => "OsuMania",
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
		};
	}

	/// <summary>Replaces every character some operating system refuses in a file name with an underscore.</summary>
	private static string Portable(string name)
	{
		return string.Create(name.Length, name, static (span, source) =>
		{
			for (var i = 0; i < source.Length; i++)
				span[i] = source[i] < ' ' || UnportableChars.Contains(source[i]) ? '_' : source[i];
		});
	}
}