using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Basil.Application.Beatmaps.Services;
using Basil.Infrastructure.Beatmaps.Services;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Infrastructure.Common.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps.Tests.Services;

/// <summary>Finding, verifying and staging new versions of the osu! rulesets from a package feed.</summary>
public sealed class RulesetUpdaterTests : IDisposable
{
	private const string NewVersion = "2999.101.0";
	private readonly string _root = Path.Combine(Path.GetTempPath(), "basil-ruleset-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_root)) Directory.Delete(_root, true);
	}

	private RulesetStore NewStore()
	{
		return new RulesetStore(new DataPaths(Options.Create(new StorageOptions { Location = _root })));
	}

	private static byte[] Package(string ruleset)
	{
		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
		{
			foreach (var framework in new[] { "net8.0", "net10.0" })
			{
				// A fixed time keeps the bytes, and so the hash, the same on every call.
				var created = zip.CreateEntry($"lib/{framework}/osu.Game.Rulesets.{ruleset}.dll");
				created.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
				using var entry = created.Open();
				entry.Write(Encoding.ASCII.GetBytes($"{ruleset}-{framework}"));
			}
		}

		return stream.ToArray();
	}

	private static string Sha512(byte[] bytes)
	{
		return Convert.ToBase64String(SHA512.HashData(bytes));
	}

	/// <summary>
	///     A feed that offers <see cref="NewVersion" />; <paramref name="corrupt" /> publishes a wrong hash for one
	///     package.
	/// </summary>
	private static HttpClient Feed(string latest, string? corrupt = null)
	{
		return new HttpClient(new FeedHandler(latest, corrupt));
	}

	[Fact]
	public async Task NewerStable_PrereleaseIgnored()
	{
		var updater = new RulesetUpdater(NewStore(), Feed(NewVersion), NullLogger<RulesetUpdater>.Instance);

		var check = await updater.CheckAsync(TestContext.Current.CancellationToken);

		Assert.Equal(NewVersion, check.Latest);
		Assert.True(check.UpdateAvailable);
	}

	[Fact]
	public async Task VerifiedStage_LibrariesStaged()
	{
		var store = NewStore();
		var updater = new RulesetUpdater(store, Feed(NewVersion), NullLogger<RulesetUpdater>.Instance);

		var staged = await updater.StageAsync(TestContext.Current.CancellationToken);

		Assert.True(staged.Staged);
		Assert.Equal(NewVersion, updater.Version().Staged);
		foreach (var ruleset in RulesetStore.Rulesets)
			Assert.Equal($"{ruleset}-net10.0",
				File.ReadAllText(Path.Combine(store.FolderOf(NewVersion), RulesetStore.LibraryName(ruleset))));
		Assert.Equal(RulesetStore.Rulesets.Length, Directory.GetFiles(store.FolderOf(NewVersion)).Length);
	}

	[Fact]
	public async Task HashMismatch_Refused()
	{
		var store = NewStore();
		var updater = new RulesetUpdater(store, Feed(NewVersion, "Mania"), NullLogger<RulesetUpdater>.Instance);

		await Assert.ThrowsAsync<RulesetUpdateException>(() => updater.StageAsync(TestContext.Current.CancellationToken));

		Assert.Null(updater.Version().Staged);
		Assert.False(Directory.Exists(store.FolderOf(NewVersion)));
		Assert.Empty(Directory.EnumerateFileSystemEntries(store.Root));
	}

	[Fact]
	public async Task ActiveVersion_NothingStaged()
	{
		var store = NewStore();
		var updater = new RulesetUpdater(store, Feed(store.Active), NullLogger<RulesetUpdater>.Instance);

		var staged = await updater.StageAsync(TestContext.Current.CancellationToken);

		Assert.False(staged.Staged);
		Assert.Null(updater.Version().Staged);
	}

	[Fact]
	public async Task AfterStaging_NotOffered()
	{
		var updater = new RulesetUpdater(NewStore(), Feed(NewVersion), NullLogger<RulesetUpdater>.Instance);
		await updater.StageAsync(TestContext.Current.CancellationToken);

		var check = await updater.CheckAsync(TestContext.Current.CancellationToken);

		Assert.False(check.UpdateAvailable);
	}

	private sealed class FeedHandler(string latest, string? corrupt) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken ct)
		{
			var path = request.RequestUri!.AbsolutePath;
			var text = path switch
			{
				_ when path.EndsWith("/index.json") => JsonSerializer.Serialize(new
					{ versions = new[] { "2020.1.0", latest, $"{latest}-beta" } }),
				_ when path.Contains("/registration5-gz-semver2/") =>
					JsonSerializer.Serialize(new { catalogEntry = "https://feed.test/catalog/" + path.Split('/')[^2] }),
				_ when path.StartsWith("/catalog/") => JsonSerializer.Serialize(new
				{
					packageHash = RulesetOf(path) == corrupt ? "AAAA" : Sha512(Package(RulesetOf(path))),
					packageHashAlgorithm = "SHA512"
				}),
				_ => null
			};
			if (text is not null)
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{ Content = new StringContent(text) });

			var bytes = Package(RulesetFrom(path));
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{ Content = new ByteArrayContent(bytes) });
		}

		private static string RulesetOf(string path)
		{
			return Capitalize(path.Split('/')[^1]);
		}

		private static string RulesetFrom(string path)
		{
			return Capitalize(path.Split('/')[^3]);
		}

		private static string Capitalize(string id)
		{
			var name = id.Replace(".json", "").Split('.')[^1];
			return char.ToUpperInvariant(name[0]) + name[1..];
		}
	}
}