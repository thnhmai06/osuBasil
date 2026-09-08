using System.Text.Json;

namespace Basil.Server.Shared.Localization;

/// <summary>One slice's on-disk locale JSON file, flattened into dotted keys mapped to their text.</summary>
internal sealed class LocaleFragment
{
	private LocaleFragment(string filePath, IReadOnlyDictionary<string, string> entries)
	{
		FilePath = filePath;
		Entries = entries;
	}

	/// <summary>The file this fragment was loaded from, used to name the file in a duplicate-key error.</summary>
	public string FilePath { get; }

	/// <summary>The file's dotted keys mapped to their text.</summary>
	public IReadOnlyDictionary<string, string> Entries { get; }

	/// <summary>Reads and flattens the JSON file at <paramref name="filePath" />.</summary>
	public static LocaleFragment Load(string filePath)
	{
		var root = JsonDocument.Parse(File.ReadAllBytes(filePath)).RootElement;
		var entries = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (key, value) in LocaleKey.Flatten(root))
			entries[key] = value;
		return new LocaleFragment(filePath, entries);
	}
}
