using System.Diagnostics;
using System.Runtime.Loader;
using Basil.Infrastructure.Common.Storage;

namespace Basil.Infrastructure.Beatmaps.Storage;

/// <summary>Keeps the downloaded ruleset libraries under the data directory and loads the staged ones at startup.</summary>
internal sealed class RulesetStore(DataPaths paths)
{
	/// <summary>The package id prefix and the rulesets, each shipped as its own package and library.</summary>
	public const string PackagePrefix = "ppy.osu.Game.Rulesets.";

	/// <summary>The rulesets of the osu! game modes.</summary>
	public static readonly string[] Rulesets = ["Osu", "Taiko", "Catch", "Mania"];

	private string? _loaded;

	/// <summary>Gets the folder that holds one folder per staged version and the pointer file.</summary>
	public string Root => Path.Combine(paths.Root, "Rulesets");

	private string Pointer => Path.Combine(Root, "current");

	/// <summary>Gets the version in use: the staged one when it was loaded at startup, else the one shipped with Basil.</summary>
	public string Active =>
		_loaded ?? Clean(FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, LibraryName("Osu")))
			.ProductVersion);

	/// <summary>Gets the library file name of a ruleset.</summary>
	public static string LibraryName(string ruleset)
	{
		return $"osu.Game.Rulesets.{ruleset}.dll";
	}

	/// <summary>Gets the version staged for the next start, or <see langword="null" /> when it is the active one.</summary>
	public string? Staged()
	{
		var staged = Current();
		return staged is null || staged == Active ? null : staged;
	}

	/// <summary>Makes a downloaded version the one to load at the next start.</summary>
	/// <param name="version">The version, whose folder is already in place.</param>
	public void Stage(string version)
	{
		var temporary = Pointer + ".tmp";
		File.WriteAllText(temporary, version);
		File.Move(temporary, Pointer, true);
	}

	/// <summary>Gets the folder of one staged version.</summary>
	public string FolderOf(string version)
	{
		return Path.Combine(Root, version);
	}

	/// <summary>Loads the staged libraries instead of the shipped ones; must run before any ruleset type is used.</summary>
	/// <returns>The version loaded, or <see langword="null" /> when none is staged or its files are incomplete.</returns>
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