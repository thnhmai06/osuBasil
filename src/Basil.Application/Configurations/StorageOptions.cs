namespace Basil.Application.Configurations;

/// <summary>
///     Names of the local folders that store user- and beatmapset-supplied files.
/// </summary>
/// <remarks>
///     Replays, avatars, beatmapsets, seasonal backgrounds, and FAQ entries each have a dedicated
///     folder, and each one backs a local file-serving endpoint on this fully-offline server. The
///     paths are fixed at startup rather than configurable: Infrastructure's DI composition root
///     resolves each one against the executable's directory (not the process's working directory)
///     and constructs this type directly, so it is never bound from IConfiguration.
/// </remarks>
public sealed class StorageOptions
{
	/// <summary>Gets or sets the folder that stores replay files.</summary>
	public required string ReplaysPath { get; init; }

	/// <summary>Gets or sets the folder that stores user avatar files.</summary>
	public required string AvatarsPath { get; init; }

	/// <summary>Gets or sets the folder that stores uploaded beatmap set files (.osz).</summary>
	public required string BeatmapsetsPath { get; init; }

	/// <summary>Gets or sets the folder that stores seasonal background files.</summary>
	public required string MenuSeasonalsPath { get; init; }

	/// <summary>Gets or sets the folder that stores main-menu banner image files.</summary>
	public required string MenuBannersPath { get; init; }

	/// <summary>Gets or sets the folder that stores FAQ entry files.</summary>
	public required string FaqsPath { get; init; }

	/// <summary>Gets or sets the root folder for the derived-response cache.</summary>
	/// <remarks>
	///     Holds resized thumbnails and transcoded audio previews, laid out as
	///     <c>{CachePath}/{endpoint}/{relativePath}</c> and consumed by
	///     <see cref="Abstractions.Storage.IResponseCache" />. Entries are written lazily on a cache
	///     miss and regenerated on demand, so the folder is safe to delete at any time.
	/// </remarks>
	public required string CachePath { get; init; }
}