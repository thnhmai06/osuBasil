using System.Security.Cryptography;
using System.Text.Json;
using Basil.Infrastructure.Rulesets.Storage;

namespace ManifestTool;

/// <summary>Maps loaded assemblies to the packages they come from, using the dependency record of this program.</summary>
internal static class Deps
{
	private static readonly string[] Roots =
	[
		"ppy.osu.Game", "ppy.osu.Game.Rulesets.Osu", "ppy.osu.Game.Rulesets.Taiko", "ppy.osu.Game.Rulesets.Catch",
		"ppy.osu.Game.Rulesets.Mania"
	];

	public static RulesetManifest Build(IEnumerable<string> loadedNames)
	{
		using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesetManifest.deps.json")));
		var libraries = document.RootElement.GetProperty("libraries");
		var target = document.RootElement.GetProperty("targets").EnumerateObject().First().Value;

		// The stack is the closure of the osu! packages; a package the server brings itself is not part of it.
		var closure = new HashSet<string>();
		var pending = new Stack<string>(target.EnumerateObject()
			.Where(p => Roots.Contains(p.Name[..p.Name.IndexOf('/')], StringComparer.OrdinalIgnoreCase)).Select(p => p.Name));
		while (pending.TryPop(out var key))
		{
			if (!closure.Add(key) || !target.TryGetProperty(key, out var entry) ||
			    !entry.TryGetProperty("dependencies", out var dependencies)) continue;

			foreach (var dependency in dependencies.EnumerateObject())
			{
				var next = target.EnumerateObject().FirstOrDefault(p => p.Name.StartsWith(dependency.Name + "/", StringComparison.OrdinalIgnoreCase));
				if (next.Name is not null) pending.Push(next.Name);
			}
		}

		var wanted = loadedNames.ToHashSet(StringComparer.Ordinal);
		var packages = new List<ManifestPackage>();
		foreach (var key in closure.Order(StringComparer.Ordinal))
		{
			if (!target.GetProperty(key).TryGetProperty("runtime", out var runtime)) continue;

			var files = runtime.EnumerateObject()
				.Select(file => file.Name)
				.Where(path => wanted.Contains(Path.GetFileNameWithoutExtension(path)))
				.Select(path => new ManifestFile(path, Hash(Path.Combine(AppContext.BaseDirectory, Path.GetFileName(path)))))
				.ToList();
			if (files.Count == 0) continue;

			var split = key.IndexOf('/');
			packages.Add(new ManifestPackage(key[..split], key[(split + 1)..], PackageHash(libraries.GetProperty(key)), files));
		}

		var duplicate = packages.SelectMany(p => p.Files).GroupBy(f => f.Name).FirstOrDefault(g => g.Count() > 1);
		if (duplicate is not null) throw new InvalidOperationException($"Two packages carry a file named {duplicate.Key}.");

		var version = packages.First(p => p.Id == "ppy.osu.Game").Version;
		return new RulesetManifest(version, packages);
	}

	// The hash NuGet stored for the package file when it took it from the feed; the dependency record holds another one.
	private static string PackageHash(JsonElement library)
	{
		var folder = library.GetProperty("path").GetString()!;
		var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
		           Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
		var name = folder.Replace('/', '.');
		return File.ReadAllText(Path.Combine(root, folder, name + ".nupkg.sha512")).Trim();
	}

	private static string Hash(string path)
	{
		using var stream = File.OpenRead(path);
		return Convert.ToHexStringLower(SHA256.HashData(stream));
	}
}
