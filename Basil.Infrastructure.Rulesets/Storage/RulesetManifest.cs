using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basil.Infrastructure.Rulesets.Storage;

/// <summary>The calculation stack the server was built against: its packages, the files taken from each and their hashes.</summary>
/// <param name="Version">The version of the osu! packages the server was compiled against.</param>
/// <param name="Packages">The packages the calculation loads assemblies from.</param>
internal sealed record RulesetManifest(string Version, IReadOnlyList<ManifestPackage> Packages)
{
	/// <summary>The name of the embedded file the manifest tool writes.</summary>
	public const string ResourceName = "rulesets.manifest.json";

	public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public IEnumerable<ManifestFile> Files => Packages.SelectMany(package => package.Files);

	public static RulesetManifest Load()
	{
		using var stream = typeof(RulesetManifest).Assembly.GetManifestResourceStream(ResourceName) ??
		                   throw new InvalidOperationException("The build carries no ruleset manifest.");
		return JsonSerializer.Deserialize<RulesetManifest>(stream, Json) ??
		       throw new InvalidOperationException("The ruleset manifest is empty.");
	}
}

/// <summary>A NuGet package of the stack, at the version the server was compiled against.</summary>
/// <param name="Id">The package id.</param>
/// <param name="Version">The package version.</param>
/// <param name="Sha512">The SHA-512 of the package file, in base 64, as the feed published it.</param>
/// <param name="Files">The files taken from the package.</param>
internal sealed record ManifestPackage(string Id, string Version, string Sha512, IReadOnlyList<ManifestFile> Files);

/// <summary>An assembly taken from a package.</summary>
/// <param name="Path">The path of the file inside the package.</param>
/// <param name="Sha256">The SHA-256 of the file, in lower-case hexadecimal.</param>
internal sealed record ManifestFile(string Path, string Sha256)
{
	/// <summary>The file name the assembly has in the stack folder.</summary>
	[JsonIgnore]
	public string Name => System.IO.Path.GetFileName(Path);

	/// <summary>The assembly name the runtime asks for.</summary>
	[JsonIgnore]
	public string AssemblyName => System.IO.Path.GetFileNameWithoutExtension(Path);
}
