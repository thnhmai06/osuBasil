using System.Text.Json;

namespace Basil.Tests.Shared.Workload.World;

/// <summary>The role an account was seeded for.</summary>
public enum AccountRole
{
	Player,
	Referee,
	Tourney,
	Developer
}

/// <summary>One seeded account loaded from <c>accounts.csv</c>.</summary>
/// <param name="Name">The exact account name.</param>
/// <param name="Password">The account password.</param>
/// <param name="Role">The role determining the account's privileges.</param>
public sealed record AccountEntry(string Name, string Password, AccountRole Role);

/// <summary>One beatmap in <c>world-data.json</c>.</summary>
public sealed record BeatmapEntry(
	int Id,
	int SetId,
	string Md5,
	string? StoryboardMd5,
	double LengthSeconds,
	int Mode,
	string Version,
	string FileName);

/// <summary>One beatmapset in <c>world-data.json</c>.</summary>
public sealed record BeatmapsetEntry(
	int Id,
	string Artist,
	string Title,
	bool HasStoryboard,
	IReadOnlyList<BeatmapEntry> Beatmaps);

/// <summary>The immutable server catalogue discovered by seeding and read from <c>world-data.json</c>.</summary>
public sealed record WorldData(
	IReadOnlyList<BeatmapsetEntry> Beatmapsets,
	IReadOnlyList<string> Seasonals,
	IReadOnlyList<string> Faqs)
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	/// <summary>Gets every beatmap across all beatmapsets.</summary>
	public IReadOnlyList<BeatmapEntry> AllBeatmaps =>
		[.. Beatmapsets.SelectMany(set => set.Beatmaps)];

	/// <summary>Loads <c>world-data.json</c> from disk.</summary>
	public static WorldData Load(string path)
	{
		using var stream = File.OpenRead(path);
		return JsonSerializer.Deserialize<WorldData>(stream, Json)
		       ?? throw new InvalidDataException($"Could not read world data from '{path}'.");
	}

	/// <summary>Loads accounts from <c>accounts.csv</c> (<c>name,password,role</c>; role defaults to <see cref="AccountRole.Player"/>).</summary>
	public static IReadOnlyList<AccountEntry> LoadAccounts(string path)
	{
		var accounts = new List<AccountEntry>();
		foreach (var raw in File.ReadLines(path))
		{
			var line = raw.Trim();
			if (line.Length == 0 || line.StartsWith('#')) continue;

			var parts = line.Split(',');
			if (parts.Length < 2) continue;
			if (parts[0].Equals("name", StringComparison.OrdinalIgnoreCase)) continue;

			var role = parts.Length >= 3 && Enum.TryParse<AccountRole>(parts[2].Trim(), true, out var parsed)
				? parsed
				: AccountRole.Player;
			accounts.Add(new AccountEntry(parts[0].Trim(), parts[1].Trim(), role));
		}

		return accounts;
	}
}
