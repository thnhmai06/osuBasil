using System.Diagnostics.CodeAnalysis;

namespace Basil.Domain.Utilities;

public static class Md5
{
	public static bool IsValid([NotNullWhen(true)] string? md5)
	{
		return md5 is { Length: 32 }
		       && md5.All(static c => c is >= '0' and <= '9'
			       or >= 'a' and <= 'f'
			       or >= 'A' and <= 'F');
	}
}