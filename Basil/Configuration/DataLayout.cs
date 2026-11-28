using System.Text.Json;

namespace Basil.Configuration;

/// <summary>The program cannot start on the <c>Data/</c> it finds.</summary>
public sealed class DataLayoutException(string message) : Exception(message);

/// <summary>Versions the layout of <c>Data/</c> in <c>metadata.json</c> and brings an older layout up to the one this program knows.</summary>
public static class DataLayout
{
	/// <summary>The layout this program writes and reads.</summary>
	public const int Current = 1;

	private const string FileName = "metadata.json";

	// Step N moves layout N to N+1; each step can run again without harm.
	private static readonly (string Name, Action<string> Run)[] Steps =
	[
		("0 to 1: remove what moved to Cache/", MoveRebuiltToCache)
	];

	public static void Ensure()
	{
		var data = ConfigurationSetup.Directory;
		var file = Path.Combine(data, FileName);
		var layout = Read(file);
		if (layout > Current)
			throw new DataLayoutException(
				$"The Data folder has layout {layout}, but this Basil only knows layout {Current}. Update Basil, or restore a Data folder made by this version.");

		for (; layout < Current; layout++)
		{
			try
			{
				Steps[layout].Run(data);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				throw new DataLayoutException(
					$"The Data folder could not be brought to layout {layout + 1} (step {Steps[layout].Name}): {exception.Message}");
			}

			if (layout + 1 < Current) Write(file, layout + 1);
		}

		if (Read(file) != Current) Write(file, Current);
	}

	private static int Read(string file)
	{
		if (!File.Exists(file)) return 0;

		try
		{
			using var document = JsonDocument.Parse(File.ReadAllText(file));
			return document.RootElement.GetProperty("layout").GetInt32();
		}
		catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException
			                                  or FormatException)
		{
			throw new DataLayoutException($"{file} cannot be read; fix or remove it.");
		}
	}

	private static void Write(string file, int layout)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(file)!);
		var temporary = file + ".tmp";
		File.WriteAllText(temporary, $"{{ \"layout\": {layout} }}");
		File.Move(temporary, file, true);
	}

	// Covers, difficulty attributes and previews are rebuilt on demand, so the old copies are dropped.
	private static void MoveRebuiltToCache(string data)
	{
		Delete(Path.Combine(data, "Rulesets", "Attributes"));
		Delete(Path.Combine(data, "Cache"));
		var sets = Path.Combine(data, "Beatmapsets");
		if (!Directory.Exists(sets)) return;

		foreach (var set in Directory.EnumerateDirectories(sets))
			Delete(Path.Combine(set, "covers"));
	}

	private static void Delete(string folder)
	{
		if (Directory.Exists(folder)) Directory.Delete(folder, true);
	}
}
