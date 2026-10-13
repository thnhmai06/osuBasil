using System.Buffers;
using System.Globalization;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores the raw .osr bytes of scores' replays, one file per score, named like osu! names an exported replay.</summary>
internal sealed class FileReplayStorage(
	DataPaths paths,
	IUserRepository users,
	IBeatmapRepository beatmaps) : IReplayStorage
{
	private DataStorage Files => paths.Replays;

	/// <inheritdoc />
	public async Task SaveAsync(Score score, Stream content, CancellationToken cancellationToken = default)
	{
		var name = await NameOfAsync(score, cancellationToken);
		if (Find(score.Id) is { } old && old != name)
			Files.Delete(old);
		await Files.SaveAsync(name, content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(Score score, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(Find(score.Id) is { } name ? Files.Open(name) : null);
	}

	private string? Find(int id)
	{
		var prefix = id.ToString(CultureInfo.InvariantCulture);
		return Files.Search(prefix + "*.osr").FirstOrDefault(name =>
			name.EndsWith(".osr", StringComparison.Ordinal) &&
			name.Length > prefix.Length &&
			name[prefix.Length] is ' ' or '.' &&
			name.StartsWith(prefix, StringComparison.Ordinal));
	}

	private async Task<string> NameOfAsync(Score score, CancellationToken cancellationToken)
	{
		var user = score.Value.UserId is { } userId ? await users.GetAsync(userId, cancellationToken) : null;
		var beatmap = score.Value.BeatmapHash is { } hash ? await beatmaps.GetAsync(hash, cancellationToken) : null;
		if (user is null || beatmap is null)
			return $"{score.Id}.osr";

		var name = string.Format(CultureInfo.InvariantCulture,
			"{0} {1} - {2} ({3:yyyy-MM-dd}) {4}",
			score.Id, user.Value.Name, beatmap.Value.FullName, score.Value.Timestamp.UtcDateTime,
			ModeName(score.Value.Mode));
		return Portable(name) + ".osr";
	}

	private static string ModeName(GameMode mode) => mode switch
	{
		GameMode.Standard => "Osu",
		GameMode.Taiko => "Taiko",
		GameMode.Catch => "CatchTheBeat",
		GameMode.Mania => "OsuMania",
		_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
	};

	/// <summary>Replaces every character some operating system refuses in a file name with an underscore.</summary>
	private static string Portable(string name)
	{
		return string.Create(name.Length, name, static (span, source) =>
		{
			for (var i = 0; i < source.Length; i++)
				span[i] = source[i] < ' ' || UnportableChars.Contains(source[i]) ? '_' : source[i];
		});
	}

	private static readonly SearchValues<char> UnportableChars = SearchValues.Create("<>:\"/\\|?*");
}