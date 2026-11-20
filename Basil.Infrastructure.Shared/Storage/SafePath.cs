using Basil.Domain.Content;
using Basil.Shared.Utilities;

namespace Basil.Infrastructure.Shared.Storage;

internal static class SafePath
{
	public static string Combine(string root, string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (!PathValidation.IsValidPath(name))
			throw new InvalidFileNameException();

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