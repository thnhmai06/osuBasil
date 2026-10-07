using System.Runtime.CompilerServices;

namespace Basil.Domain.Utilities;

/// <summary>Rules that every text the server keeps follows.</summary>
public static class TextExtensions
{
	/// <summary>Tells whether a text holds the NUL character, which no kept text may contain.</summary>
	public static bool HasNul(this string value) => value.Contains('\0');

	/// <summary>Returns the text unchanged, or throws when it holds the NUL character.</summary>
	/// <exception cref="ArgumentException">The text holds the NUL character.</exception>
	public static string ThrowIfHasNul(this string value, [CallerArgumentExpression(nameof(value))] string? paramName = null) =>
		value.HasNul() ? throw new ArgumentException("Text cannot contain the NUL character.", paramName) : value;
}
