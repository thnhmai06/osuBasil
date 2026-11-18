using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Basil.Shared.Utilities;

namespace Basil.Domain.Content;

public readonly record struct ContentReference : IParsable<ContentReference>, IFormattable
{
	public ContentReference(Uri address)
	{
		ArgumentNullException.ThrowIfNull(address);
		if (!address.IsHttp())
			throw new ArgumentException("The address must be absolute with an http or https scheme.", nameof(address));
		Value = address.AbsoluteUri;
	}

	public ContentReference(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (!PathValidation.TryNormalizePath(path, out var canonical))
			throw new ArgumentException("The path must be a canonical relative asset path.", nameof(path));
		Value = canonical;
	}

	public string Value { get; }

	public bool IsOffline => Value is not null && !IsOnline(Value);

	public string ToString(string? format, IFormatProvider? formatProvider)
	{
		if (!string.IsNullOrEmpty(format) && format != "G")
			throw new FormatException($"The '{format}' format is not supported.");

		return ToString();
	}

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

	public static explicit operator ContentReference(string text)
	{
		return Parse(text, CultureInfo.InvariantCulture);
	}

	public static explicit operator ContentReference(Uri address)
	{
		return new ContentReference(address);
	}

	public static explicit operator string(ContentReference reference)
	{
		return reference.ToString();
	}

	public override string ToString()
	{
		return Value ?? string.Empty;
	}

	private static bool IsOnline(string s)
	{
		return Uri.TryCreate(s, UriKind.Absolute, out var u) && u.IsHttp();
	}
}