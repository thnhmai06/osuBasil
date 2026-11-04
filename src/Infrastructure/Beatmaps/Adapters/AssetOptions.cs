namespace Basil.Infrastructure.Beatmaps.Adapters;

/// <summary>Configures the capability ports that extract beatmap assets.</summary>
public sealed class AssetOptions
{
	/// <summary>
	///     Gets or sets the directory for the temporary files made while audio previews are built; empty means the
	///     <c>Previews</c> folder of the server's cache.
	/// </summary>
	public string CacheDirectory { get; set; } = string.Empty;

	/// <summary>The folder holding the ffmpeg executable, or null to find it on the PATH.</summary>
	public string? FfmpegFolder { get; set; }
}