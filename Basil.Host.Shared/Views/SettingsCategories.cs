using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Shared.Settings;
using Basil.Host.Shared.Json;

namespace Basil.Host.Shared.Views;

public static class SettingsCategories
{
	private static readonly Dictionary<string, (Func<ServerSettings, object> Get,
			Func<ServerSettings, JsonNode, ServerSettings> Apply)>
		Categories = new(StringComparer.OrdinalIgnoreCase)
		{
			["general"] = Category(s => s.General, (s, v) => s with { General = v }),
			["accounts"] = Category(s => s.Accounts, (s, v) => s with { Accounts = v }),
			["locks"] = Category(s => LocksView.Of(s.Locks), (s, v) => s with { Locks = v.ToLocks() }),
			["sessions"] = Category(s => s.Sessions, (s, v) => s with { Sessions = v }),
			["rateLimits"] = Category(s => s.RateLimits, (s, v) => s with { RateLimits = v }),
			["uploads"] = Category(s => s.Uploads, (s, v) => s with { Uploads = v }),
			["beatmaps"] = Category(s => s.Beatmaps, (s, v) => s with { Beatmaps = v }),
			["multiplayer"] = Category(s => s.Multiplayer, (s, v) => s with { Multiplayer = v }),
			["bot"] = Category(s => s.Bot, (s, v) => s with { Bot = v }),
			["cors"] = Category(s => s.Cors, (s, v) => s with { Cors = v })
		};

	public static IEnumerable<string> Names => Categories.Keys;

	public static bool Exists(string category)
	{
		return Categories.ContainsKey(category);
	}

	public static object Get(string category, ServerSettings settings)
	{
		return Categories[category].Get(settings);
	}

	public static IReadOnlyDictionary<string, object> GetAll(ServerSettings settings)
	{
		return Categories.ToDictionary(c => c.Key, c => c.Value.Get(settings));
	}

	public static ServerSettings Patch(string category, ServerSettings settings, JsonObject patch)
	{
		var entry = Categories[category];
		var merged = JsonSerializer.SerializeToNode(entry.Get(settings), ApiJson.Options)!;
		Merge(merged, patch);
		return entry.Apply(settings, merged);
	}

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