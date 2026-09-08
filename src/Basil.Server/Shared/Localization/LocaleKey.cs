using System.Text.Json;

namespace Basil.Server.Shared.Localization;

/// <summary>Flattens a nested locale JSON document into dotted hierarchical keys.</summary>
internal static class LocaleKey
{
	/// <summary>
	///     Walks <paramref name="element" />'s properties, joining nested object names with <c>.</c> so a
	///     naturally nested document (a category, then a member within it) becomes a flat sequence of
	///     dotted keys mapped to their string value.
	/// </summary>
	public static IEnumerable<(string Key, string Value)> Flatten(JsonElement element, string prefix = "")
	{
		foreach (var property in element.EnumerateObject())
		{
			var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
			if (property.Value.ValueKind == JsonValueKind.Object)
			{
				foreach (var nested in Flatten(property.Value, key))
					yield return nested;
			}
			else
			{
				yield return (key, property.Value.GetString() ??
					throw new InvalidOperationException($"'{key}' is not a string value."));
			}
		}
	}
}
