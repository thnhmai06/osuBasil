using System.Runtime.CompilerServices;

namespace Basil.Domain.Shared;

public static class StringExtensions
{
	extension(string value)
	{
		public bool HasNul()
		{
			return value.Contains('\0');
		}

		public string ThrowIfHasNul([CallerArgumentExpression(nameof(value))] string? paramName = null)
		{
			return value.HasNul()
				? throw new ArgumentException("Text cannot contain the NUL character.", paramName)
				: value;
		}
	}
}