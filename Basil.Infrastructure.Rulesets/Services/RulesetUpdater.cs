using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Rulesets.Services;
using Basil.Infrastructure.Rulesets.Storage;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Rulesets.Services;

internal sealed class RulesetUpdater(RulesetStore store, IHttpClientFactory clients, ILogger<RulesetUpdater> logger)
	: IRulesetUpdater
{
	private const string Feed = "https://api.nuget.org/v3-flatcontainer";

	// Larger than any file of the stack; a package that claims more is not what the manifest names.
	private const long MaxFileBytes = 256L * 1024 * 1024;

	private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

	private readonly SemaphoreSlim _busy = new(1, 1);

	public RulesetVersions Version()
	{
		return new RulesetVersions(store.Version, store.State);
	}

	public async Task<RulesetInstallResult?> InstallAsync(CancellationToken ct = default)
	{
		if (!await _busy.WaitAsync(0, ct)) return null;
		try
		{
			if (store.Check() == RulesetState.Installed) return new RulesetInstallResult(store.Version, false, false);

			// Once an osu! type was asked for, the process keeps what it has; the repair takes effect after a restart.
			var restart = store.InUse;
			Directory.CreateDirectory(store.Root);
			var work = Path.Combine(store.Root, ".staging-" + Guid.NewGuid().ToString("N"));
			var files = Path.Combine(work, "files");
			Directory.CreateDirectory(files);
			try
			{
				using var http = clients.CreateClient();
				http.Timeout = Timeout;
				foreach (var package in store.Manifest.Packages) await FetchAsync(http, package, work, files, ct);

				if (Directory.Exists(store.Folder)) Directory.Move(store.Folder, Path.Combine(work, "replaced"));
				Directory.Move(files, store.Folder);
			}
			finally
			{
				Directory.Delete(work, true);
			}

			if (store.Check() != RulesetState.Installed)
				throw new RulesetUpdateException("The installed files do not match the build.");

			logger.LogInformation("Installed the osu! calculation stack {Version}", store.Version);
			return new RulesetInstallResult(store.Version, true, restart);
		}
		finally
		{
			_busy.Release();
		}
	}

	private static async Task FetchAsync(HttpClient http, ManifestPackage package, string work, string files,
		CancellationToken ct)
	{
		var id = package.Id.ToLowerInvariant();
		var version = package.Version.ToLowerInvariant();
		var archive = Path.Combine(work, id + ".nupkg");
		try
		{
			using (var response = await http.GetAsync($"{Feed}/{id}/{version}/{id}.{version}.nupkg",
				       HttpCompletionOption.ResponseHeadersRead, ct))
			{
				response.EnsureSuccessStatusCode();
				await using var target = File.Create(archive);
				await response.Content.CopyToAsync(target, ct);
			}

			await using (var stream = File.OpenRead(archive))
			{
				var actual = Convert.ToBase64String(await SHA512.HashDataAsync(stream, ct));
				if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual),
					    Encoding.ASCII.GetBytes(package.Sha512)))
					throw new RulesetUpdateException($"{package.Id} {package.Version}: the package does not match its hash.");
			}

			using var zip = ZipFile.OpenRead(archive);
			foreach (var file in package.Files)
			{
				var entry = zip.GetEntry(file.Path) ?? throw new RulesetUpdateException(
					$"{package.Id} {package.Version}: the package holds no {file.Path}.");
				if (entry.Length > MaxFileBytes)
					throw new RulesetUpdateException($"{package.Id} {package.Version}: {file.Name} is too large.");

				var target = Path.Combine(files, file.Name);
				entry.ExtractToFile(target);
				await using var extracted = File.OpenRead(target);
				if (Convert.ToHexStringLower(await SHA256.HashDataAsync(extracted, ct)) != file.Sha256)
					throw new RulesetUpdateException(
						$"{package.Id} {package.Version}: {file.Name} does not match its hash.");
			}
		}
		catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException
			                          or TaskCanceledException && !ct.IsCancellationRequested)
		{
			throw new RulesetUpdateException($"{package.Id} {package.Version} could not be downloaded: {e.Message}", e);
		}
	}
}
