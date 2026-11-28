using System.Globalization;
using System.Text.Json;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;

namespace Basil.Infrastructure.Rulesets.Adapters;

internal static class AttributeFile
{
	public static string Write(string version, DifficultyAttributes attributes, BeatmapSnapshot info)
	{
		var values = new Dictionary<string, double>();
		foreach (var (id, value) in attributes.ToDatabaseAttributes())
			if (value is not null)
				values[id.ToString(CultureInfo.InvariantCulture)] =
					Convert.ToDouble(value, CultureInfo.InvariantCulture);

		return JsonSerializer.Serialize(new Content(version, info, values));
	}

	public static BeatmapSnapshot? TryRead(string text, string version, DifficultyAttributes attributes)
	{
		try
		{
			if (JsonSerializer.Deserialize<Content>(text) is not { Attributes: { } values, Info: { } info } content ||
			    content.Version != version)
				return null;

			attributes.FromDatabaseAttributes(
				values.ToDictionary(pair => int.Parse(pair.Key, CultureInfo.InvariantCulture), pair => pair.Value),
				new StoredOnlineInfo(info));
			return info;
		}
		catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
		{
			return null;
		}
	}

	private sealed record Content(string Version, BeatmapSnapshot Info, Dictionary<string, double> Attributes);
}