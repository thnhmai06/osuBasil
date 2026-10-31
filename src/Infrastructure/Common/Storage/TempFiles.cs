namespace Basil.Infrastructure.Common.Storage;

/// <summary>Names temporary files for saves in progress.</summary>
public static class TempFiles
{
	/// <summary>Names a temporary file in the directory of a target file; the name shares nothing with the target's.</summary>
	/// <param name="path">The absolute path of the file the temporary file stands in for.</param>
	/// <returns>The absolute path of the temporary file.</returns>
	public static string Name(string path)
	{
		return Path.Combine(Path.GetDirectoryName(path)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
	}
}