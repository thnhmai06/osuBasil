using System.Diagnostics;
using System.Runtime.Loader;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Beatmaps.Storage;

internal sealed class RulesetStore(DataPaths paths)
{
	public const string PackagePrefix = "ppy.osu.Game.Rulesets.";

	public static readonly string[] Rulesets = ["Osu", "Taiko", "Catch", "Mania"];

	private string? _loaded;

	public string Root => Path.Combine(paths.Root, "Rulesets");

	private string Pointer => Path.Combine(Root, "current");

	public string Active =>
		_loaded ?? Clean(FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, LibraryName("Osu")))
			.ProductVersion);

	public static string LibraryName(string ruleset)
	{
		return $"osu.Game.Rulesets.{ruleset}.dll";
	}

	public string? Staged()
	{
		var staged = Current();
		return staged is null || staged == Active ? null : staged;
	}

	public void Stage(string version)
	{
		var temporary = Pointer + ".tmp";
		File.WriteAllText(temporary, version);
		File.Move(temporary, Pointer, true);
	}

	public string FolderOf(string version)
	{
		return Path.Combine(Root, version);
	}

	public string? LoadStaged()
	{
		var version = Current();
		if (version is null || version == Clean(FileVersionInfo.GetVersionInfo(
			    Path.Combine(AppContext.BaseDirectory, LibraryName("Osu"))).ProductVersion)) return null;

		var files = Rulesets.Select(name => Path.Combine(FolderOf(version), LibraryName(name))).ToArray();
		if (!files.All(File.Exists)) return null;

		foreach (var file in files) AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
		return _loaded = version;
	}

	private string? Current()
	{
		return File.Exists(Pointer) && File.ReadAllText(Pointer).Trim() is { Length: > 0 } version ? version : null;
	}

	private static string Clean(string? productVersion)
	{
		var text = productVersion ?? "unknown";
		var plus = text.IndexOf('+');
		return plus < 0 ? text : text[..plus];
	}
}