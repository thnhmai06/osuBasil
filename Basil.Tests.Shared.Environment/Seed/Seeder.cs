using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Basil.Tests.Shared.Clients;
using Basil.Tests.Shared.Clients.Rest;

namespace Basil.Tests.Shared.Environment.Seed;

/// <summary>Parameters controlling account creation and world discovery during seeding.</summary>
public sealed record SeedOptions(
	Endpoint Endpoint,
	string OwnerUsername,
	string OwnerPassword,
	string AccountsOutputPath,
	string WorldDataOutputPath,
	int PlayerCount = 1500,
	int RefereeCount = 100,
	int TourneyCount = 400,
	int ExpectedBeatmapsets = 15,
	int Seed = 20261125,
	TimeSpan? ImportWaitTimeout = null);

/// <summary>Summary of a completed seed run.</summary>
public sealed record SeedReport(
	int AccountsWritten,
	int AccountsCreated,
	int AccountsExisting,
	int Beatmapsets,
	int Beatmaps,
	int Seasonals,
	int Faqs,
	bool RateLimitRestored);

/// <summary>
///     Prepares the test bench strictly through Basil's public HTTP API: verifies health and rulesets, creates
///     accounts, restores rate limits and writes <c>accounts.csv</c> and <c>world-data.json</c>.
/// </summary>
public sealed class Seeder(HttpClient http)
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

	/// <summary>Runs the full T2 seeding sequence.</summary>
	public async Task<SeedReport> RunAsync(SeedOptions options, CancellationToken ct)
	{
		var rest = new RestClient(options.Endpoint, http);

		// 1. Check health.
		var health = await rest.GetAsync("/health", ct);
		if (!health.Success)
			throw new InvalidOperationException($"Health check failed ({(int)health.Status}): {health.Raw}");

		// 2. Sign in as Owner.
		var login = await rest.SignInAsync(options.OwnerUsername, options.OwnerPassword, ct);
		if (!login.Success || rest.Token is null)
			throw new InvalidOperationException($"Owner sign-in failed ({(int)login.Status}): {login.Message}");

		try
		{
			// 3. Verify the osu! calculation stack is installed.
			var ruleset = await rest.GetAsync("/v1/settings/ruleset/version", ct);
			if (!ruleset.Success)
				throw new InvalidOperationException(
					$"Ruleset version check failed ({(int)ruleset.Status}): {ruleset.Message}");
			var state = ruleset.Data.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
			if (!string.Equals(state, "installed", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					$"The osu! calculation stack is not installed (state: '{state ?? "unknown"}').");

			// 4. Wait until all expected beatmapsets are imported.
			var sets = await WaitForBeatmapsetsAsync(rest, options.ExpectedBeatmapsets,
				options.ImportWaitTimeout ?? TimeSpan.FromMinutes(10), ct);

			// 5. Read current rateLimits, raise rest.mutations temporarily, create accounts, then restore defaults.
			var originalLimits = await rest.GetAsync("/v1/settings/rateLimits", ct);
			if (!originalLimits.Success)
				throw new InvalidOperationException(
					$"Could not read rateLimits ({(int)originalLimits.Status}): {originalLimits.Message}");

			var originalCount = 60;
			var originalWindow = "00:01:00";
			if (originalLimits.Data.TryGetProperty("rest", out var restNode) &&
			    restNode.TryGetProperty("mutations", out var mutNode))
			{
				if (mutNode.TryGetProperty("count", out var c)) originalCount = c.GetInt32();
				if (mutNode.TryGetProperty("window", out var w) && w.GetString() is { Length: > 0 } ws)
					originalWindow = ws;
			}

			var raise = await rest.PatchAsync("/v1/settings/rateLimits",
				new { rest = new { mutations = new { count = 100_000, window = originalWindow } } }, ct);
			if (!raise.Success)
				throw new InvalidOperationException(
					$"Could not raise rest.mutations ({(int)raise.Status}): {raise.Message}");

			var accounts = BuildAccountList(options);
			var created = 0;
			var existing = 0;
			var existingByRole = new Dictionary<string, SeedAccount>();
			var restored = false;

			try
			{
				foreach (var account in accounts)
				{
					ct.ThrowIfCancellationRequested();
					var reply = await rest.PostAsync("/v1/users", new
					{
						username = account.Name,
						password = account.Password,
						country = "VN",
						privileges = PrivilegesOf(account.Role)
					}, ct);

					if (reply.Success)
					{
						created++;
					}
					else if (reply.Status == HttpStatusCode.Conflict)
					{
						existing++;
						existingByRole.TryAdd(account.Role, account);
					}
					else
					{
						throw new InvalidOperationException(
							$"Creating account '{account.Name}' failed ({(int)reply.Status} {reply.ErrorCode}): {reply.Message}");
					}
				}
			}
			finally
			{
				// Always put rest.mutations back to its original value.
				using var restoreTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
				var restoreReply = await rest.PatchAsync("/v1/settings/rateLimits",
					new { rest = new { mutations = new { count = originalCount, window = originalWindow } } },
					restoreTimeout.Token);
				var verify = await rest.GetAsync("/v1/settings/rateLimits", restoreTimeout.Token);
				restored = restoreReply.Success && verify.Success &&
				           verify.Data.TryGetProperty("rest", out var vRest) &&
				           vRest.TryGetProperty("mutations", out var vMut) &&
				           vMut.TryGetProperty("count", out var vCount) &&
				           vCount.GetInt32() == originalCount;
			}

			if (!restored)
				throw new InvalidOperationException(
					"Failed to restore rateLimits.rest.mutations to its default value.");

			// An account left by an earlier seed keeps its old password: prove the new accounts.csv still signs in.
			foreach (var account in existingByRole.Values)
				await VerifySignInAsync(options.Endpoint, account, ct);

			// 6. Discover beatmaps with exact md5 and storyboard md5 from the public API archives.
			var discoveredSets = new List<SeedBeatmapset>(sets.Count);
			foreach (var setId in sets)
			{
				var detail = await DiscoverSetAsync(rest, options.Endpoint, setId, ct);
				if (detail is not null) discoveredSets.Add(detail);
			}

			var seasonals = await ReadStringListAsync(rest, "/v1/menu/seasonals", ct);
			var faqs = await ReadStringListAsync(rest, "/v1/faqs", ct);

			// 7. Write accounts.csv and world-data.json outside git.
			WriteAccountsCsv(options.AccountsOutputPath, accounts);
			WriteWorldDataJson(options.WorldDataOutputPath, new SeedWorldData(discoveredSets, seasonals, faqs));

			var totalBeatmaps = discoveredSets.Sum(s => s.Beatmaps.Count);
			return new SeedReport(accounts.Count, created, existing, discoveredSets.Count, totalBeatmaps,
				seasonals.Count, faqs.Count, restored);
		}
		finally
		{
			try
			{
				using var outTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				await rest.SignOutAsync(outTimeout.Token);
			}
			catch (Exception)
			{
				// Best effort.
			}
		}
	}

	private async Task VerifySignInAsync(Endpoint endpoint, SeedAccount account, CancellationToken ct)
	{
		var check = new RestClient(endpoint, http);
		var reply = await check.SignInAsync(account.Name, account.Password, ct);
		if (!reply.Success)
			throw new InvalidOperationException(
				$"Account '{account.Name}' ({account.Role}) already exists with a different password ({(int)reply.Status}). " +
				"It was created by an earlier seed with another --seed value: seed with that value again, or reset the database.");

		try
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			await check.SignOutAsync(timeout.Token);
		}
		catch (Exception)
		{
			// Best effort.
		}
	}

	private static List<SeedAccount> BuildAccountList(SeedOptions options)
	{
		var accounts = new List<SeedAccount>(options.PlayerCount + options.RefereeCount + options.TourneyCount + 1);
		for (var i = 1; i <= options.PlayerCount; i++)
			accounts.Add(new SeedAccount($"load_p_{i:D4}", PasswordOf(options.Seed, "p", i), "player"));
		for (var i = 1; i <= options.RefereeCount; i++)
			accounts.Add(new SeedAccount($"load_r_{i:D3}", PasswordOf(options.Seed, "r", i), "referee"));
		for (var i = 1; i <= options.TourneyCount; i++)
			accounts.Add(new SeedAccount($"load_t_{i:D4}", PasswordOf(options.Seed, "t", i), "tourney"));
		accounts.Add(new SeedAccount("load_dev", PasswordOf(options.Seed, "dev", 1), "developer"));
		return accounts;
	}

	private static string PasswordOf(int seed, string tag, int index)
	{
		var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{tag}:{index}")));
		return $"Basil!{hash[..12]}";
	}

	private static string[] PrivilegesOf(string role)
	{
		return role switch
		{
			"referee" or "tourney" => ["player", "supporter", "tournament"],
			"developer" => ["player", "supporter", "developer"],
			_ => ["player", "supporter"]
		};
	}

	private static async Task<List<int>> WaitForBeatmapsetsAsync(RestClient rest, int expectedCount, TimeSpan timeout,
		CancellationToken ct)
	{
		var deadline = DateTimeOffset.UtcNow + timeout;
		while (true)
		{
			var ids = new List<int>();
			var offset = 0;
			const int limit = 100;
			while (true)
			{
				var reply = await rest.GetAsync($"/v1/beatmapsets?offset={offset}&limit={limit}", ct);
				if (!reply.Success)
					throw new InvalidOperationException(
						$"Listing beatmapsets failed ({(int)reply.Status}): {reply.Message}");

				if (reply.Data.ValueKind == JsonValueKind.Array)
					foreach (var item in reply.Data.EnumerateArray())
						if (item.TryGetProperty("id", out var idProp))
							ids.Add(idProp.GetInt32());

				var hasNext = reply.Meta.ValueKind == JsonValueKind.Object &&
				              reply.Meta.TryGetProperty("hasNext", out var nextProp) &&
				              nextProp.ValueKind == JsonValueKind.True;
				if (!hasNext) break;
				offset += limit;
			}

			if (ids.Count >= expectedCount || DateTimeOffset.UtcNow >= deadline)
			{
				if (ids.Count < expectedCount)
					throw new TimeoutException(
						$"Expected {expectedCount} beatmapsets but only {ids.Count} were imported.");
				return ids;
			}

			await Task.Delay(TimeSpan.FromSeconds(3), ct);
		}
	}

	private async Task<SeedBeatmapset?> DiscoverSetAsync(RestClient rest, Endpoint endpoint, int setId,
		CancellationToken ct)
	{
		var detail = await rest.GetAsync($"/v1/beatmapsets/{setId}", ct);
		if (!detail.Success || detail.Data.ValueKind != JsonValueKind.Object) return null;

		var setNode = detail.Data.GetProperty("set");
		var artist = setNode.GetProperty("artist").GetString() ?? string.Empty;
		var title = setNode.GetProperty("title").GetString() ?? string.Empty;
		var hasStoryboard = setNode.TryGetProperty("hasStoryboard", out var sbProp) &&
		                    sbProp.ValueKind == JsonValueKind.True;

		// Download the no-video .osz through the public REST API to compute exact .osu and .osb MD5 hashes.
		var (osuEntries, storyboardMd5) = await ReadArchiveHashesAsync(endpoint, rest.Token!, setId, ct);

		var maps = new List<SeedBeatmap>();
		foreach (var mapNode in detail.Data.GetProperty("beatmaps").EnumerateArray())
		{
			var id = mapNode.GetProperty("id").GetInt32();
			var version = mapNode.GetProperty("version").GetString() ?? string.Empty;
			var mode = ParseMode(mapNode.GetProperty("mode"));
			var lengthSeconds = mapNode.TryGetProperty("lengthSeconds", out var lenProp) &&
			                    lenProp.ValueKind == JsonValueKind.Number
				? lenProp.GetDouble()
				: 60.0;

			var matched = osuEntries.FirstOrDefault(e => e.BeatmapId == id)
			              ?? osuEntries.FirstOrDefault(e =>
				              string.Equals(e.Version, version, StringComparison.OrdinalIgnoreCase));
			if (matched is null) continue;

			maps.Add(new SeedBeatmap(id, setId, matched.Md5, hasStoryboard ? storyboardMd5 : null,
				lengthSeconds, mode, version, matched.FileName));
		}

		return new SeedBeatmapset(setId, artist, title, hasStoryboard, maps);
	}

	private async Task<(List<OsuArchiveEntry> Maps, string? StoryboardMd5)> ReadArchiveHashesAsync(
		Endpoint endpoint, string token, int setId, CancellationToken ct)
	{
		using var request = endpoint.Request(HttpMethod.Get, "api", $"/v1/beatmapsets/{setId}/download?noVideo=1");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();

		await using var memory = new MemoryStream();
		await using (var remote = await response.Content.ReadAsStreamAsync(ct))
		{
			await remote.CopyToAsync(memory, ct);
		}

		memory.Position = 0;
		await using var zip = new ZipArchive(memory, ZipArchiveMode.Read, false);
		var maps = new List<OsuArchiveEntry>();
		string? storyboardMd5 = null;

		foreach (var entry in zip.Entries)
			if (entry.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
			{
				await using var entryStream = await entry.OpenAsync(ct);
				using var buffer = new MemoryStream();
				await entryStream.CopyToAsync(buffer, ct);
				var bytes = buffer.ToArray();
				var md5 = Convert.ToHexStringLower(MD5.HashData(bytes));
				var (beatmapId, version) = ParseOsuHeader(bytes);
				maps.Add(new OsuArchiveEntry(entry.Name, md5, beatmapId, version));
			}
			else if (storyboardMd5 is null && entry.FullName.EndsWith(".osb", StringComparison.OrdinalIgnoreCase))
			{
				await using var entryStream = await entry.OpenAsync(ct);
				storyboardMd5 = Convert.ToHexStringLower(await MD5.HashDataAsync(entryStream, ct));
			}

		return (maps, storyboardMd5);
	}

	private static (int BeatmapId, string Version) ParseOsuHeader(byte[] bytes)
	{
		var beatmapId = 0;
		var version = string.Empty;
		using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8);
		while (reader.ReadLine() is { } line)
			if (line.StartsWith("BeatmapID:", StringComparison.OrdinalIgnoreCase))
				_ = int.TryParse(line["BeatmapID:".Length..].Trim(), out beatmapId);
			else if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
				version = line["Version:".Length..].Trim();
			else if (line.StartsWith("[HitObjects]", StringComparison.OrdinalIgnoreCase))
				break;

		return (beatmapId, version);
	}

	private static int ParseMode(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Number) return element.GetInt32();
		return element.GetString()?.ToLowerInvariant() switch
		{
			"taiko" => 1,
			"catch" => 2,
			"mania" => 3,
			_ => 0
		};
	}

	private static async Task<List<string>> ReadStringListAsync(RestClient rest, string path, CancellationToken ct)
	{
		var reply = await rest.GetAsync(path, ct);
		if (!reply.Success || reply.Data.ValueKind != JsonValueKind.Array) return [];
		return
		[
			.. reply.Data.EnumerateArray().Select(e => e.GetString()).OfType<string>()
				.Where(s => !string.IsNullOrWhiteSpace(s))
		];
	}

	private static void WriteAccountsCsv(string path, IReadOnlyList<SeedAccount> accounts)
	{
		var dir = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
		writer.WriteLine("name,password,role");
		foreach (var account in accounts)
			writer.WriteLine($"{account.Name},{account.Password},{account.Role}");
	}

	private static void WriteWorldDataJson(string path, SeedWorldData data)
	{
		var dir = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(path, JsonSerializer.Serialize(data, Json), new UTF8Encoding(false));
	}

	private sealed record SeedAccount(string Name, string Password, string Role);

	private sealed record OsuArchiveEntry(string FileName, string Md5, int BeatmapId, string Version);

	private sealed record SeedBeatmap(
		int Id,
		int SetId,
		string Md5,
		string? StoryboardMd5,
		double LengthSeconds,
		int Mode,
		string Version,
		string FileName);

	private sealed record SeedBeatmapset(
		int Id,
		string Artist,
		string Title,
		bool HasStoryboard,
		IReadOnlyList<SeedBeatmap> Beatmaps);

	private sealed record SeedWorldData(
		IReadOnlyList<SeedBeatmapset> Beatmapsets,
		IReadOnlyList<string> Seasonals,
		IReadOnlyList<string> Faqs);
}