using System.Diagnostics.CodeAnalysis;

namespace Basil.Domain.Content;

/// <summary>A canonical reference to an asset: the address of an asset hosted elsewhere, or the path of a file the server stores under its data directory.</summary>
/// <remarks>The default value refers to nothing; a model that holds a reference rejects it.</remarks>
public readonly record struct AssetReference : IParsable<AssetReference>, IFormattable
{
	/// <summary>Gets the canonical text of the reference; null for the default value.</summary>
	public string Value { get; }

	/// <summary>Initializes a reference to an asset hosted at an absolute address.</summary>
	/// <param name="address">The absolute http or https address of the asset.</param>
	/// <exception cref="ArgumentNullException"><paramref name="address" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="address" /> is not absolute, or its scheme is not http or https.</exception>
	public AssetReference(Uri address)
	{
		if (address is null) throw new ArgumentNullException(nameof(address));
		if (!address.IsAbsoluteUri
		    || address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException("The address must be absolute with an http or https scheme.", nameof(address));
		Value = address.AbsoluteUri;
	}

	/// <summary>Initializes a reference to a file the server stores under its data directory.</summary>
	/// <param name="path">The canonical path of the file, relative to the data directory.</param>
	/// <exception cref="ArgumentNullException"><paramref name="path" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="path" /> is empty, not a relative path, or not canonical.</exception>
	public AssetReference(string path)
	{
		if (path is null) throw new ArgumentNullException(nameof(path));
		if (!TryNormalizePath(path, out var canonical))
			throw new ArgumentException("The path must be a canonical relative asset path.", nameof(path));
		Value = canonical;
	}

	/// <summary>Gets a value that indicates whether the reference names a file the server stores, rather than an address hosted elsewhere.</summary>
	public bool IsStored => Value is not null && !IsAddress(Value);

	/// <summary>Parses a reference from its canonical text.</summary>
	/// <param name="s">The text to parse.</param>
	/// <param name="provider">A provider that supplies culture-specific formatting information.</param>
	/// <returns>The parsed reference.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="s" /> is <see langword="null" />.</exception>
	/// <exception cref="FormatException"><paramref name="s" /> is not a canonical reference.</exception>
	public static AssetReference Parse(string s, IFormatProvider? provider)
	{
		if (s is null) throw new ArgumentNullException(nameof(s));
		try
		{
			return IsAddress(s)
				? new AssetReference(new Uri(s, UriKind.Absolute))
				: new AssetReference(s);
		}
		catch (ArgumentException exception)
		{
			throw new FormatException("The text is not a canonical asset reference.", exception);
		}
	}

	/// <summary>Tries to parse a reference from its canonical text.</summary>
	/// <param name="s">The text to parse.</param>
	/// <param name="provider">A provider that supplies culture-specific formatting information.</param>
	/// <param name="result">The parsed reference, or the default value when the text is invalid.</param>
	/// <returns><see langword="true" /> when <paramref name="s" /> is a canonical reference; otherwise, <see langword="false" />.</returns>
	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AssetReference result)
	{
		if (s is null)
		{
			result = default;
			return false;
		}
		if (IsAddress(s))
		{
			result = new AssetReference(new Uri(s, UriKind.Absolute));
			return true;
		}
		if (TryNormalizePath(s, out var canonical))
		{
			result = new AssetReference(canonical);
			return true;
		}
		result = default;
		return false;
	}

	/// <inheritdoc />
	public override string ToString() => Value ?? string.Empty;

	/// <summary>Returns the canonical text of the reference.</summary>
	/// <param name="format">The format to use, or <see langword="null" /> for the default.</param>
	/// <param name="formatProvider">A provider that supplies culture-specific formatting information.</param>
	/// <returns>The canonical text of the reference.</returns>
	/// <exception cref="FormatException"><paramref name="format" /> is not empty or "G".</exception>
	public string ToString(string? format, IFormatProvider? formatProvider)
	{
		if (format is null || format.Length == 0 || format == "G")
			return ToString();
		throw new FormatException("The format is not supported.");
	}

	/// <summary>Converts the canonical text of a reference to a reference.</summary>
	/// <param name="text">The text to convert.</param>
	public static explicit operator AssetReference(string text) => Parse(text, null);

	/// <summary>Converts an absolute address to a reference.</summary>
	/// <param name="address">The address to convert.</param>
	public static explicit operator AssetReference(Uri address) => new(address);

	/// <summary>Converts a reference to its canonical text.</summary>
	/// <param name="reference">The reference to convert.</param>
	public static explicit operator string(AssetReference reference) => reference.ToString();

	private static bool IsAddress(string s)
	{
		return Uri.TryCreate(s, UriKind.Absolute, out var u)
		       && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
	}

	private static bool TryNormalizePath(string path, [NotNullWhen(true)] out string? canonical)
	{
		canonical = null;
		var trimmed = path.Trim();
		if (trimmed.Length == 0) return false;
		foreach (var c in trimmed)
		{
			if (c == '\0' || char.IsControl(c)) return false;
		}
		var text = trimmed.Replace('\\', '/');
		if (text.StartsWith('/') || text.Contains(':')) return false;

		var segments = text.Split('/');
		var parts = new List<string>(segments.Length);
		foreach (var segment in segments)
		{
			if (segment.Length == 0 || segment == ".") continue;
			if (segment == "..") return false;
			parts.Add(segment);
		}
		if (parts.Count == 0) return false;

		canonical = string.Join('/', parts);
		return true;
	}
}
