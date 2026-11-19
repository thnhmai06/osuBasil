using System.Text;

namespace Basil.Infrastructure.Shared.Storage;

public static class FileNames
{
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