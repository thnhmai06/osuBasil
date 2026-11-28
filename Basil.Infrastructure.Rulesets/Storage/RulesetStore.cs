using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Basil.Application.Rulesets.Services;
using Basil.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Rulesets.Storage;

/// <summary>
///     Keeps the osu! calculation stack in <c>Data/Rulesets/&lt;version&gt;</c>, checks it against the build manifest
///     and serves its assemblies to the runtime. Holds no osu! type, so it works while the stack is missing.
/// </summary>
internal sealed class RulesetStore(DataPaths paths, ILogger<RulesetStore> logger)
{
	private readonly RulesetManifest _manifest = RulesetManifest.Load();
	private readonly Lock _gate = new();
	private HashSet<string> _names = [];
	private volatile bool _loaded;
	private volatile bool _requestedWhileOff;
	private volatile RulesetState _state = RulesetState.Missing;
	private RulesetState? _logged;

	public string Version => _manifest.Version;

	public RulesetManifest Manifest => _manifest;

	public string Root => Path.Combine(paths.Root, "Rulesets");

	public string Folder => Path.Combine(Root, Version);

	public RulesetState State => _state;

	public bool Installed => _state == RulesetState.Installed;

	/// <summary>True once the runtime has asked for a stack assembly: osu! types are in use, so a repair needs a restart.</summary>
	public bool InUse => _loaded || _requestedWhileOff;

	/// <summary>Registers the assembly resolution and checks the folder; called before any osu! type is used.</summary>
	public void Initialize()
	{
		_names = _manifest.Files.Select(file => file.AssemblyName).ToHashSet(StringComparer.Ordinal);
		AssemblyLoadContext.Default.Resolving += Resolve;
		Check();
	}

	/// <summary>Checks the files of the stack against the manifest and logs the state.</summary>
	public RulesetState Check()
	{
		lock (_gate)
		{
			_state = Verify();
			if (_state == _logged) return _state;

			_logged = _state;
			if (_state == RulesetState.Installed)
				logger.LogInformation("The osu! calculation stack {Version} is installed", Version);
			else
				logger.LogWarning(
					"The osu! calculation stack {Version} is {State}: star ratings, pp and beatmapset imports are off. Run `ruleset update`",
					Version, _state);
			return _state;
		}
	}

	private RulesetState Verify()
	{
		if (!Directory.Exists(Folder)) return RulesetState.Missing;

		var missing = 0;
		foreach (var file in _manifest.Files)
		{
			var path = Path.Combine(Folder, file.Name);
			if (!File.Exists(path)) missing++;
			else if (!HashMatches(path, file)) return RulesetState.Damaged;
		}

		return missing == 0 ? RulesetState.Installed : missing == _manifest.Files.Count() ? RulesetState.Missing : RulesetState.Damaged;
	}

	private Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
	{
		if (name.Name is not { } simple || !_names.Contains(simple)) return null;

		if (!Installed)
		{
			_requestedWhileOff = true;
			return null;
		}

		var file = _manifest.Files.First(item => item.AssemblyName == simple);
		var path = Path.Combine(Folder, file.Name);
		// Checked again at the load, as the volume is writable.
		if (!HashMatches(path, file))
		{
			_state = RulesetState.Damaged;
			logger.LogError("{File} of the osu! calculation stack does not match the build; the stack is turned off", file.Name);
			return null;
		}

		_loaded = true;
		return context.LoadFromAssemblyPath(path);
	}

	private static bool HashMatches(string path, ManifestFile file)
	{
		try
		{
			using var stream = File.OpenRead(path);
			return Convert.ToHexStringLower(SHA256.HashData(stream)) == file.Sha256;
		}
		catch (IOException)
		{
			return false;
		}
	}
}
