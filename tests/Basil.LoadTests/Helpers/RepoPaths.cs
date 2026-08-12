namespace Basil.LoadTests.Helpers;

/// <summary>Resolves paths relative to the repository root, regardless of the executable's output directory.</summary>
public static class RepoPaths
{
	private static readonly Lazy<string> Root = new(FindRoot);

	/// <summary>Gets the repository root, located by walking up from the executable until <c>Basil.slnx</c> is found.</summary>
	public static string RepoRoot => Root.Value;

	/// <summary>Resolves <paramref name="path" /> against the repository root when it is not already rooted.</summary>
	/// <param name="path">An absolute or repo-relative path.</param>
	/// <returns>The resolved absolute path.</returns>
	public static string Resolve(string path)
	{
		return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(RepoRoot, path));
	}

	private static string FindRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "Basil.slnx"))) return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException(
			$"Could not locate the repository root (Basil.slnx) above '{AppContext.BaseDirectory}'.");
	}
}