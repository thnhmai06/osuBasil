using System.Buffers;
using System.Globalization;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Storage;
using Basil.Application.Users.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Domain.Client;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Scores.Storage;

/// <summary>Stores the replays of scores as complete .osr files, one per score, named like osu! names an exported replay.</summary>
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
	public async Task SaveAsync(ScoreRecord score, OsuVersion clientVersion, Stream content)
	{
		var user = score.Value.UserId is { } userId ? await users.GetAsync(userId) : null;
		var beatmap = score.Value.BeatmapHash is { } hash ? await beatmaps.GetAsync(hash) : null;

		var name = NameOf(score, user, beatmap);
		await Files.SaveAsync(name,
			(output, token) => ReplayFile.WriteAsync(output, score, user?.Value.Name ?? string.Empty, clientVersion,
				content, token), CancellationToken.None);
		foreach (var previous in Find(score.Id).Where(previous => previous != name).ToList())
			Files.Delete(previous);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(ScoreRecord score, CancellationToken ct = default)
	{
		return Task.FromResult<Stream?>(Find(score.Id).FirstOrDefault() is { } name ? Files.Open(name) : null);
	}

	/// <inheritdoc />
	public async Task<Stream?> OpenFramesAsync(ScoreRecord score, CancellationToken ct = default)
	{
		if (await OpenAsync(score, ct) is not { } file) return null;

		if (ReplayFile.TryFindFrames(file, out var offset, out var length))
			return new SliceStream(file, offset, length);

		await file.DisposeAsync();
		return null;
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

	private static string NameOf(ScoreRecord score, UserRecord? user, BeatmapRecord? beatmap)
	{
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