using System.Text;

namespace Basil.Common.Infrastructure.Files;

/// <summary>Shapes names so that common file systems accept them.</summary>
public static class FileNames
{
	/// <summary>Shortens a name so that it takes at most a number of UTF-8 bytes, without splitting a character.</summary>
	/// <param name="name">The name to shorten.</param>
	/// <param name="maxBytes">The most bytes the result may take when encoded as UTF-8.</param>
	/// <returns>The name itself when it fits; otherwise its longest fitting start, without trailing spaces and dots.</returns>
	public static string Fitted(string name, int maxBytes)
	{
		if (Encoding.UTF8.GetByteCount(name) <= maxBytes) return name;

		var length = name.Length;
		while (length > 0 &&
		       (Encoding.UTF8.GetByteCount(name.AsSpan(0, length)) > maxBytes ||
		        char.IsHighSurrogate(name[length - 1])))
			length--;
		return name[..length].TrimEnd(' ', '.');
	}
}