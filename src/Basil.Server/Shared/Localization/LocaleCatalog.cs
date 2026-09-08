using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Basil.Server.Shared.Localization;

/// <summary>
///     The merged, resolved locale for the running server: every slice's fragment loaded once and
///     addressed by a dotted key that mirrors the command hierarchy it belongs to.
/// </summary>
/// <remarks>
///     Fragments live beside the slice that owns their text, so a slice's wording and its code
///     change together. A key that no fragment supplies throws the first time it is resolved;
///     startup touches every reply-constant holder so that failure surfaces at boot rather than
///     mid-request.
/// </remarks>
public static class LocaleCatalog
{
	private static readonly Lazy<FrozenDictionary<string, string>> Entries = new(Load);
	private static readonly ConcurrentDictionary<string, byte> Referenced = new();

	/// <summary>Every key the merged catalog holds.</summary>
	public static IReadOnlyCollection<string> AllKeys => Entries.Value.Keys;

	/// <summary>Every key production code has asked for, since process start.</summary>
	public static IReadOnlyCollection<string> ReferencedKeys => (IReadOnlyCollection<string>)Referenced.Keys;

	/// <summary>Resolves <paramref name="key" /> to its localized text.</summary>
	/// <param name="key">A dotted hierarchical key, such as <c>Commands.Mp.In.NotScopedToAnyMatch</c>.</param>
	/// <exception cref="InvalidOperationException">No loaded fragment defines <paramref name="key" />.</exception>
	public static string Get(string key)
	{
		Referenced[key] = 0;
		if (Entries.Value.TryGetValue(key, out var value)) return value;
		throw new InvalidOperationException($"The active locale is missing '{key}'.");
	}

	private static FrozenDictionary<string, string> Load()
	{
		var directory = Path.Combine(AppContext.BaseDirectory, "Data", "Localization");
		var merged = new Dictionary<string, string>(StringComparer.Ordinal);
		var owner = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
		{
			var fragment = LocaleFragment.Load(file);
			foreach (var (key, value) in fragment.Entries)
			{
				if (!merged.TryAdd(key, value))
					throw new InvalidOperationException(
						$"Two locale fragments both define '{key}': {owner[key]} and {fragment.FilePath}.");
				owner[key] = fragment.FilePath;
			}
		}

		return merged.ToFrozenDictionary(StringComparer.Ordinal);
	}
}
