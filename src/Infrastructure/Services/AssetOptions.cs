namespace Basil.Infrastructure.Services;

/// <summary>Configures the capability ports that extract beatmap assets.</summary>
public sealed class AssetOptions
{
	/// <summary>Gets or sets the directory where audio previews of beatmapsets are kept.</summary>
	public required string CacheDirectory { get; set; }

	/// <summary>The folder holding the ffmpeg executable, or null to find it on the PATH.</summary>
	public string? FfmpegFolder { get; set; }
}