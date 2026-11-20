using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Basil.Shared.Utilities;

namespace Basil.Domain.Content;

/// <summary>A reference to a content asset: either an absolute http or https address, or a canonical relative asset path.</summary>
public readonly record struct ContentReference : IParsable<ContentReference>, IFormattable
{
	/// <summary>Creates a reference to an online address.</summary>
	/// <param name="address">An absolute http or https address.</param>
	/// <exception cref="ArgumentException">The address is not an absolute http or https address.</exception>
	public ContentReference(Uri address)
	{
		ArgumentNullException.ThrowIfNull(address);
		if (!address.IsHttp())
			throw new ArgumentException("The address must be absolute with an http or https scheme.", nameof(address));
		Value = address.AbsoluteUri;
	}

	/// <summary>Creates a reference to a stored asset.</summary>
	/// <param name="path">A canonical relative asset path.</param>
	/// <exception cref="ArgumentException">The path is blank or is not a canonical relative asset path.</exception>
	public ContentReference(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (!PathValidation.TryNormalizePath(path, out var canonical))
			throw new ArgumentException("The path must be a canonical relative asset path.", nameof(path));
		Value = canonical;
	}

	/// <summary>The address or the path the reference holds; <see langword="null" /> only on a default value.</summary>
	public string Value { get; }

	/// <summary>Whether the reference names a stored asset rather than an online address.</summary>
	public bool IsOffline => Value is not null && !IsOnline(Value);

	public string ToString(string? format, IFormatProvider? formatProvider)
	{
		if (!string.IsNullOrEmpty(format) && format != "G")
			throw new FormatException($"The '{format}' format is not supported.");

		return ToString();
	}

	/// <summary>Reads a reference from text: an http or https address, or else a canonical relative asset path.</summary>
	/// <param name="s">The text to read.</param>
	/// <param name="provider">Ignored.</param>
	/// <returns>The reference.</returns>
	/// <exception cref="FormatException">The text is neither.</exception>
	public static ContentReference Parse(string s, IFormatProvider? provider)
	{
		ArgumentNullException.ThrowIfNull(s);
		try
		{
			return IsOnline(s)
				? new ContentReference(new Uri(s, UriKind.Absolute))
				: new ContentReference(s);
		}
		catch (ArgumentException exception)
		{
			throw new FormatException("The text is not a canonical asset reference.", exception);
		}
	}

	/// <summary>Reads a reference from text like <see cref="Parse" /> but reports failure instead of throwing.</summary>
	/// <param name="s">The text to read.</param>
	/// <param name="provider">Ignored.</param>
	/// <param name="result">The reference, or a default value on failure.</param>
	/// <returns><see langword="true" /> when the text was read.</returns>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ContentReference result)
	{
		result = default;
		if (s is null) return false;

		try
		{
			result = Parse(s, provider);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Reads a reference from text like <see cref="Parse" />.</summary>
	/// <param name="text">The text to read.</param>
	public static explicit operator ContentReference(string text)
	{
		return Parse(text, CultureInfo.InvariantCulture);
	}

	/// <summary>Creates a reference to an online address.</summary>
	/// <param name="address">An absolute http or https address.</param>
	public static explicit operator ContentReference(Uri address)
	{
		return new ContentReference(address);
	}

	/// <summary>Gets the address or path of a reference as text.</summary>
	/// <param name="reference">The reference.</param>
	public static explicit operator string(ContentReference reference)
	{
		return reference.ToString();
	}

	/// <summary>Gets the address or path, or an empty text on a default value.</summary>
	public override string ToString()
	{
		return Value ?? string.Empty;
	}

	private static bool IsOnline(string s)
	{
		return Uri.TryCreate(s, UriKind.Absolute, out var u) && u.IsHttp();
	}
}