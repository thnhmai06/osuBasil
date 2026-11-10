using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Basil.Application.Beatmaps.Services;
using Basil.Infrastructure.Beatmaps.Storage;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Services;

/// <summary>Downloads the osu! ruleset packages from the NuGet feed, verifies their hash and stages their libraries.</summary>
internal sealed class RulesetUpdater(RulesetStore store, HttpClient http, ILogger<RulesetUpdater> logger)
	: IRulesetUpdater
{
	private const string Feed = "https://api.nuget.org/v3";

	/// <inheritdoc />
	public RulesetVersions Version()
	{
		return new RulesetVersions(store.Active, store.Staged());
	}

	/// <inheritdoc />
	public async Task<RulesetCheck> CheckAsync(CancellationToken ct = default)
	{
		return new RulesetCheck(Version(), await LatestAsync(ct));
	}

	/// <inheritdoc />
	public async Task<RulesetStageResult> StageAsync(CancellationToken ct = default)
	{
		var check = await CheckAsync(ct);
		if (!check.UpdateAvailable) return new RulesetStageResult(check.Latest, false);

		var version = check.Latest;
		Directory.CreateDirectory(store.Root);
		var work = Path.Combine(store.Root, ".staging-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(work);
		try
		{
			var packages = new List<(string Ruleset, string File)>();
			foreach (var ruleset in RulesetStore.Rulesets)
			{
				var id = (RulesetStore.PackagePrefix + ruleset).ToLowerInvariant();
				var expected = await ExpectedHashAsync(id, version, ct);
				var file = Path.Combine(work, id + ".nupkg");
				await DownloadAsync($"{Feed}-flatcontainer/{id}/{version}/{id}.{version}.nupkg", file,
					ct);
				if (!await HashMatchesAsync(file, expected, ct))
					throw new RulesetUpdateException(
						$"The package {id} {version} does not match the hash the feed publishes.");

				packages.Add((ruleset, file));
			}

			var folder = Path.Combine(work, "files");
			Directory.CreateDirectory(folder);
			foreach (var (ruleset, file) in packages) Extract(ruleset, file, folder);

			var target = store.FolderOf(version);
			if (Directory.Exists(target)) Directory.Delete(target, true);
			Directory.Move(folder, target);
			store.Stage(version);
			logger.LogInformation("Staged the osu! rulesets {Version}; they take effect after a restart", version);
			return new RulesetStageResult(version, true);
		}
		catch (Exception e) when (e is HttpRequestException or JsonException or IOException or InvalidDataException)
		{
			throw new RulesetUpdateException("The osu! rulesets could not be downloaded: " + e.Message, e);
		}
		finally
		{
			Directory.Delete(work, true);
		}
	}

	private async Task<string> LatestAsync(CancellationToken ct)
	{
		try
		{
			var id = (RulesetStore.PackagePrefix + RulesetStore.Rulesets[0]).ToLowerInvariant();
			var index = await http.GetFromJsonAsync<VersionIndex>($"{Feed}-flatcontainer/{id}/index.json",
				ct);
			return index?.Versions.LastOrDefault(version => !version.Contains('-'))
			       ?? throw new RulesetUpdateException("The feed lists no stable version of the osu! rulesets.");
		}
		catch (Exception e) when (e is HttpRequestException or JsonException)
		{
			throw new RulesetUpdateException("The osu! ruleset feed could not be read: " + e.Message, e);
		}
	}

	/// <summary>Reads the SHA-512 the feed publishes for a package (base64).</summary>
	private async Task<string> ExpectedHashAsync(string id, string version, CancellationToken ct)
	{
		var leaf = await http.GetFromJsonAsync<RegistrationLeaf>($"{Feed}/registration5-gz-semver2/{id}/{version}.json",
			ct);
		var entry = leaf is null
			? null
			: await http.GetFromJsonAsync<CatalogEntry>(leaf.CatalogEntry, ct);
		return entry is { PackageHash: { Length: > 0 } hash, PackageHashAlgorithm: "SHA512" }
			? hash
			: throw new RulesetUpdateException($"The feed publishes no SHA-512 hash for {id} {version}.");
	}

	private async Task DownloadAsync(string address, string file, CancellationToken ct)
	{
		using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		await using var target = File.Create(file);
		await response.Content.CopyToAsync(target, ct);
	}

	private static async Task<bool> HashMatchesAsync(string file, string expected, CancellationToken ct)
	{
		await using var stream = File.OpenRead(file);
		var actual = Convert.ToBase64String(await SHA512.HashDataAsync(stream, ct));
		return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual),
			Encoding.ASCII.GetBytes(expected));
	}

	/// <summary>Extracts only the library of the newest target framework of a package.</summary>
	private static void Extract(string ruleset, string package, string folder)
	{
		using var archive = ZipFile.OpenRead(package);
		var name = RulesetStore.LibraryName(ruleset);
		var entry = archive.Entries
			            .Where(e => e.FullName.EndsWith("/" + name, StringComparison.Ordinal))
			            .Select(e => (Entry: e, Framework: FrameworkOf(e.FullName)))
			            .Where(candidate => candidate.Framework is not null)
			            .MaxBy(candidate => candidate.Framework).Entry
		            ?? throw new RulesetUpdateException($"The package of {ruleset} holds no {name}.");
		entry.ExtractToFile(Path.Combine(folder, name));
	}

	/// <summary>Reads the version of a <c>lib/netX.Y/...</c> entry; other frameworks give <see langword="null" />.</summary>
	private static Version? FrameworkOf(string entryName)
	{
		var parts = entryName.Split('/');
		return parts is ["lib", var folder, _] && folder.StartsWith("net", StringComparison.Ordinal) &&
		       System.Version.TryParse(folder[3..], out var version)
			? version
			: null;
	}

	private sealed record VersionIndex(string[] Versions);

	private sealed record RegistrationLeaf(string CatalogEntry);

	private sealed record CatalogEntry(string? PackageHash, string? PackageHashAlgorithm);
}