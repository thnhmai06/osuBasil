// Generates Basil.Infrastructure.Rulesets/rulesets.manifest.json: runs the calculation and the beatmap reader over the
// sample beatmaps, records every assembly of the osu! packages that gets loaded, and writes where each comes from and its hash.
//
//   dotnet run --project tools/RulesetManifest              write the manifest, then prove it with a stack-only run
//   dotnet run --project tools/RulesetManifest -- --check   fail when the committed manifest differs
//   (internal) --stack <folder>                             run the trial with the stack served only from <folder>
using System.Diagnostics;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Basil.Application.Rulesets.Ports;
using Basil.Domain.Game;
using Basil.Domain.Scores;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Infrastructure.Rulesets.Adapters;
using Basil.Infrastructure.Rulesets.Storage;
using Basil.Infrastructure.Shared.Storage;
using Basil.Shared.Structures.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using ManifestTool;

if (args is ["--off", var stackFolder, var offAssets])
	return await Trial.RunOffAsync(stackFolder, offAssets);

if (args is ["--stack", var folder, var sampleAssets])
{
	// Child run: only the manifest's files are reachable, as in a server that has the stack installed.
	AssemblyLoadContext.Default.Resolving += (context, name) =>
		File.Exists(Path.Combine(folder, name.Name + ".dll"))
			? context.LoadFromAssemblyPath(Path.Combine(folder, name.Name + ".dll"))
			: null;
	await Trial.RunAsync(sampleAssets);
	return 0;
}

var root = FindRoot();
var manifestPath = Path.Combine(root, "Basil.Infrastructure.Rulesets", "rulesets.manifest.json");
var assets = Path.Combine(root, "tests", "Assets");
var loaded = new HashSet<string>();
AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
{
	lock (loaded) loaded.Add(e.LoadedAssembly.GetName().Name!);
};

await Trial.RunAsync(assets);
Console.WriteLine("Trial calculation passed.");

string[] names;
lock (loaded) names = [.. loaded];
var manifest = Deps.Build(names);
var json = JsonSerializer.Serialize(manifest, RulesetManifest.Json) + "\n";

if (args is ["--check"])
{
	if (File.ReadAllText(manifestPath) == json) return 0;
	Console.Error.WriteLine("rulesets.manifest.json is out of date; run tools/RulesetManifest.");
	return 1;
}

File.WriteAllText(manifestPath, json);
Console.WriteLine($"Wrote {manifestPath}: {manifest.Packages.Count} packages, {manifest.Packages.Sum(p => p.Files.Count)} files.");
return Verify(manifest, assets);

// Runs the trial again in a copy of this program from which the manifest's assemblies are removed and put in a folder
// of their own: the trial passes only when the manifest lists everything that is loaded.
static int Verify(RulesetManifest manifest, string assets)
{
	var work = Path.Combine(Path.GetTempPath(), "ruleset-manifest-" + Guid.NewGuid().ToString("N"));
	var copy = Path.Combine(work, "program");
	var stack = Path.Combine(work, "stack");
	try
	{
		Directory.CreateDirectory(stack);
		foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(copy, Path.GetRelativePath(AppContext.BaseDirectory, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target);
		}

		foreach (var file in manifest.Packages.SelectMany(p => p.Files))
		{
			File.Move(Path.Combine(copy, file.Name), Path.Combine(stack, file.Name));
		}

		// Without its dependency record the host takes every assembly beside the program, as a server does.
		File.Delete(Path.Combine(copy, "RulesetManifest.deps.json"));
		var info = new ProcessStartInfo("dotnet") { ArgumentList = { Path.Combine(copy, "RulesetManifest.dll"), "--stack", stack, assets } };
		using var process = Process.Start(info)!;
		process.WaitForExit();
		process.WaitForExit();
		Console.WriteLine(process.ExitCode == 0
			? "Stack-only run passed: the manifest lists every assembly the calculation loads."
			: "Stack-only run FAILED: the manifest misses an assembly.");
		if (process.ExitCode != 0) return process.ExitCode;

		// Without the stack the guarded calls answer nothing and throw nothing; installing it turns them on in the same process.
		var off = new ProcessStartInfo("dotnet") { ArgumentList = { Path.Combine(copy, "RulesetManifest.dll"), "--off", stack, assets } };
		using var offProcess = Process.Start(off)!;
		offProcess.WaitForExit();
		Console.WriteLine(offProcess.ExitCode == 0
			? "Missing-stack run passed: calls answer null, then work once the stack is installed."
			: "Missing-stack run FAILED.");
		return offProcess.ExitCode;
	}
	finally
	{
		Directory.Delete(work, true);
	}
}

static string FindRoot()
{
	for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		if (File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			return directory.FullName;

	throw new InvalidOperationException("Run this tool inside the repository.");
}
