using System.Globalization;
using System.Text.Json;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;

namespace Basil.Infrastructure.Beatmaps.Adapters;

internal static class AttributeFile
{
	public static string Write(int version, DifficultyAttributes attributes)
	{
		var values = new Dictionary<string, double>();
		foreach (var (id, value) in attributes.ToDatabaseAttributes())
			if (value is not null)
				values[id.ToString(CultureInfo.InvariantCulture)] =
					Convert.ToDouble(value, CultureInfo.InvariantCulture);

		return JsonSerializer.Serialize(new Content(version, values));
	}

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

	private sealed record Content(int Version, Dictionary<string, double> Attributes);
}