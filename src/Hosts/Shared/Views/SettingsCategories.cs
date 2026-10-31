using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Content.Persistence;

namespace Basil.Host.Shared.Views;

/// <summary>The categories of the server settings and how each one is read, changed by a JSON merge patch and replaced.</summary>
public static class SettingsCategories
{
	private static readonly Dictionary<string, (Func<ServerSettings, object> Get,
			Func<ServerSettings, JsonNode, ServerSettings> Apply)>
		Categories = new(StringComparer.OrdinalIgnoreCase)
		{
			["general"] = Category(s => s.General, (s, v) => s with { General = v }),
			["locks"] = Category(s => LocksView.Of(s.Locks), (s, v) => s with { Locks = v.ToLocks() }),
			["sessions"] = Category(s => s.Sessions, (s, v) => s with { Sessions = v }),
			["rateLimits"] = Category(s => s.RateLimits, (s, v) => s with { RateLimits = v }),
			["uploads"] = Category(s => s.Uploads, (s, v) => s with { Uploads = v }),
			["beatmaps"] = Category(s => s.Beatmaps, (s, v) => s with { Beatmaps = v }),
			["multiplayer"] = Category(s => s.Multiplayer, (s, v) => s with { Multiplayer = v }),
			["bot"] = Category(s => s.Bot, (s, v) => s with { Bot = v }),
			["cors"] = Category(s => s.Cors, (s, v) => s with { Cors = v })
		};

	/// <summary>Gets the names of the categories.</summary>
	public static IEnumerable<string> Names => Categories.Keys;

	/// <summary>Finds a category by name, ignoring case.</summary>
	public static bool Exists(string category)
	{
		return Categories.ContainsKey(category);
	}

	/// <summary>Reads one category of the settings.</summary>
	public static object Get(string category, ServerSettings settings)
	{
		return Categories[category].Get(settings);
	}

	/// <summary>Reads every category of the settings, by name.</summary>
	public static Dictionary<string, object> GetAll(ServerSettings settings)
	{
		return Categories.ToDictionary(c => c.Key, c => c.Value.Get(settings));
	}

	/// <summary>Merges a patch into one category and returns the changed settings.</summary>
	/// <exception cref="ArgumentException">The merged category is not valid.</exception>
	/// <exception cref="JsonException">The merged category cannot be read.</exception>
	public static ServerSettings Patch(string category, ServerSettings settings, JsonObject patch)
	{
		var entry = Categories[category];
		var merged = JsonSerializer.SerializeToNode(entry.Get(settings), ApiJson.Options)!;
		Merge(merged, patch);
		return entry.Apply(settings, merged);
	}

	/// <summary>Merges a patch into a node the way JSON merge patch does: objects merge, anything else replaces.</summary>
	private static void Merge(JsonNode target, JsonObject patch)
	{
		if (target is not JsonObject targetObject) return;

		foreach (var (key, value) in patch)
			if (value is JsonObject nested && targetObject[key] is JsonObject existing)
				Merge(existing, nested);
			else
				targetObject[key] = value?.DeepClone();
	}

	private static (Func<ServerSettings, object>, Func<ServerSettings, JsonNode, ServerSettings>) Category<T>(
		Func<ServerSettings, T> get, Func<ServerSettings, T, ServerSettings> set)
	{
		return (s => get(s)!, (s, node) => set(s, node.Deserialize<T>(ApiJson.Options)!));
	}
}