using Basil.Domain.Content;

namespace Basil.Infrastructure.Common.Storage;

/// <summary>Combines a stored file's name with the directory that holds it, refusing any name that escapes it.</summary>
internal static class SafePath
{
	/// <summary>Combines a name with the directory that holds it.</summary>
	/// <param name="root">The directory the file belongs to.</param>
	/// <param name="name">The file's name, relative to the directory.</param>
	/// <returns>The absolute path of the file.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="name" /> is not a valid stored file name (see <see cref="ContentReference.IsValidPath" />) or
	///     resolves outside <paramref name="root" />.
	/// </exception>
	public static string Combine(string root, string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (!ContentReference.IsValidPath(name))
			throw new ArgumentException("The name is not a valid stored file name.", nameof(name));

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