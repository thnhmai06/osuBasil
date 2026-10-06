namespace Basil.Infrastructure.Runtime;

/// <summary>Configures where beatmapset archives are imported from.</summary>
public sealed class BeatmapImportOptions
{
	/// <summary>The directory watched for beatmapset archives to import.</summary>
	public required string Directory { get; init; }
}
