namespace Basil.Domain.Client;

/// <summary>The build of an osu! client.</summary>
/// <param name="Date">The date of the build.</param>
/// <param name="Revision">The revision within that date, or <see langword="null" /> for the first build of the day.</param>
/// <param name="Stream">The release stream the build belongs to.</param>
public readonly record struct OsuVersion(DateOnly Date, int? Revision, OsuVersionStream Stream);

/// <summary>The release stream of an osu! client build.</summary>
public enum OsuVersionStream : byte
{
	/// <summary>The stable release.</summary>
	Stable,

	/// <summary>The beta release.</summary>
	Beta,

	/// <summary>The cutting-edge release.</summary>
	CuttingEdge,

	/// <summary>The tournament client release.</summary>
	Tourney,

	/// <summary>A development build.</summary>
	Dev
}