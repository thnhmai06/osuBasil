namespace Basil.Configuration;

/// <summary>Puts the files shipped with Basil into <c>Data/</c> where the operator has none, and never overwrites one.</summary>
public static class ShippedFiles
{
	public static void CopyMissing()
	{
		var shipped = Path.Combine(AppContext.BaseDirectory, "Defaults", "Data");
		if (!Directory.Exists(shipped)) return;

		foreach (var source in Directory.EnumerateFiles(shipped, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(ConfigurationSetup.Directory, Path.GetRelativePath(shipped, source));
			if (File.Exists(target)) continue;

			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			// A copy made by another start at the same time is the same file, so losing the race is no failure.
			File.Copy(source, target, false);
		}
	}
}
