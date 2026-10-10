namespace Basil.Infrastructure.Storage.Common.Queries;

/// <summary>Combines a stored file's name with the directory that holds it, refusing any name that escapes it.</summary>
internal static class SafePath
{
	/// <summary>Combines a name with the directory that holds it.</summary>
	/// <param name="root">The directory the file belongs to.</param>
	/// <param name="name">The file's name, relative to the directory.</param>
	/// <returns>The absolute path of the file.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="name" /> is empty or white space, is rooted, contains a null character or a <c>.</c> or
	///     <c>..</c> segment, or resolves outside <paramref name="root" />.
	/// </exception>
	public static string Combine(string root, string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		if (name.Contains('\0'))
			throw new ArgumentException("The name cannot contain a null character.", nameof(name));

		if (Path.IsPathRooted(name))
			throw new ArgumentException("The name must be relative.", nameof(name));

		var segments = name.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
		if (segments.Any(segment => segment is "." or ".."))
			throw new ArgumentException("The name cannot contain a '.' or '..' segment.", nameof(name));

		var fullRoot = Path.GetFullPath(root);
		var combined = Path.GetFullPath(Path.Combine(fullRoot, name));
		var boundary = fullRoot.EndsWith(Path.DirectorySeparatorChar)
			? fullRoot
			: fullRoot + Path.DirectorySeparatorChar;
		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		return combined.StartsWith(boundary, comparison)
			? combined
			: throw new ArgumentException("The name resolves outside the root directory.", nameof(name));
	}
}