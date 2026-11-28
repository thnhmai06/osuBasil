namespace Basil.Infrastructure.Beatmaps.Persistence;

/// <summary>Builds the lookup form of the name of a beatmap's .osu file.</summary>
internal static class BeatmapFileName
{
	private const string InvalidCharacters = ":\\/*<>?\"|";

	/// <summary>Gets the name of the .osu file of a difficulty, in the form it is looked up by.</summary>
	public static string Of(string artist, string title, string creator, string version)
	{
		return Normalize($"{artist} - {title} ({creator}) [{version}].osu");
	}

	/// <summary>Drops the characters a file name cannot hold and lowercases the rest.</summary>
	public static string Normalize(string fileName)
	{
		ArgumentNullException.ThrowIfNull(fileName);
		return string.Concat(fileName.Where(character => InvalidCharacters.IndexOf(character) < 0))
			.ToLowerInvariant();
	}
}
