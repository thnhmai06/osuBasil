namespace Basil.Infrastructure.Services;

/// <summary>Configures the capability ports that extract beatmap assets.</summary>
public sealed class AssetOptions
{
	/// <summary>The directory where derived beatmap assets are kept.</summary>
	public required string CacheDirectory { get; set; }

	/// <summary>The ffmpeg executable used to cut audio previews.</summary>
	public string Ffmpeg { get; set; } = "ffmpeg";
}