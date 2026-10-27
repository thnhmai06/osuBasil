namespace Basil.Infrastructure.Beatmaps.Capabilities;

/// <summary>Configures the capability ports that extract beatmap assets.</summary>
public sealed class AssetOptions
{
	/// <summary>Gets or sets the directory for the temporary files made while audio previews are built.</summary>
	public required string CacheDirectory { get; set; }

	/// <summary>The folder holding the ffmpeg executable, or null to find it on the PATH.</summary>
	public string? FfmpegFolder { get; set; }
}