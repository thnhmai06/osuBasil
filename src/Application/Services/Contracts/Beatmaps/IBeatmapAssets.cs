using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>Makes the files derived from a beatmapset's stored files, such as audio previews.</summary>
/// <remarks>
///     Everything is made when the set is indexed; reading a set's files is done through the beatmapset storage and
///     never makes anything.
/// </remarks>
public interface IBeatmapAssets
{
	/// <summary>Makes the derived files of a set: an audio preview of each audio track its difficulties use.</summary>
	/// <param name="set">The beatmapset, whose audio is already stored.</param>
	/// <param name="beatmaps">The set's difficulties, naming their audio and preview times.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>A preview that cannot be made is left out; the set stays usable without it.</remarks>
	Task BuildAsync(BeatmapsetRecord set, IReadOnlyList<BeatmapRecord> beatmaps, CancellationToken cancellationToken = default);
}