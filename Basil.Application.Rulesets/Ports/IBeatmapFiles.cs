using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Rulesets.Ports;

/// <summary>Gives the rulesets the .osu files they calculate from.</summary>
public interface IBeatmapFiles
{
	/// <summary>Opens the file of a beatmap; null when the beatmap or a file with that checksum is not held.</summary>
	/// <param name="beatmapId">The beatmap the file belongs to.</param>
	/// <param name="checksum">The MD5 hash of the file the caller expects.</param>
	ValueTask<Stream?> OpenAsync(int beatmapId, Md5Hash checksum, CancellationToken ct = default);
}