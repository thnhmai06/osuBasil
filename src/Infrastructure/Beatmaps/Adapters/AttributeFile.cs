using System.Globalization;
using System.Text.Json;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;

namespace Basil.Infrastructure.Beatmaps.Adapters;

/// <summary>
///     The text a calculated set of difficulty attributes is stored as: the version of the ruleset's calculator and the
///     (id, value) pairs the calculator itself gives for storage.
/// </summary>
/// <remarks>
///     The attribute object is not serialized as it is: the lazer object loses its object counts and its mods on the way
///     back. The counts are read again from the stored beatmap.
/// </remarks>
internal static class AttributeFile
{
	private sealed record Content(int Version, Dictionary<string, double> Attributes);

	/// <summary>Writes attributes as the text to store.</summary>
	/// <param name="version">The version of the ruleset's difficulty calculator that calculated them.</param>
	/// <param name="attributes">The calculated attributes.</param>
	/// <returns>The text.</returns>
	public static string Write(int version, DifficultyAttributes attributes)
	{
		var values = new Dictionary<string, double>();
		foreach (var (id, value) in attributes.ToDatabaseAttributes())
			if (value is not null)
				values[id.ToString(CultureInfo.InvariantCulture)] = Convert.ToDouble(value, CultureInfo.InvariantCulture);

		return JsonSerializer.Serialize(new Content(version, values));
	}

	/// <summary>Reads stored text back into attributes.</summary>
	/// <param name="text">The stored text.</param>
	/// <param name="version">The version of the ruleset's difficulty calculator now.</param>
	/// <param name="attributes">An empty attributes object of the ruleset; filled from the text.</param>
	/// <param name="online">What the stored beatmap says about its objects and difficulty.</param>
	/// <returns>
	///     <see langword="false" /> when the text is damaged or was calculated by another version, so the attributes have to be
	///     calculated again.
	/// </returns>
	public static bool TryRead(string text, int version, DifficultyAttributes attributes, IBeatmapOnlineInfo online)
	{
		try
		{
			if (JsonSerializer.Deserialize<Content>(text) is not { Attributes: { } values } content ||
			    content.Version != version)
				return false;

			attributes.FromDatabaseAttributes(
				values.ToDictionary(pair => int.Parse(pair.Key, CultureInfo.InvariantCulture), pair => pair.Value),
				online);
			return true;
		}
		catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
		{
			return false;
		}
	}
}
