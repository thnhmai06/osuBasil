using System.Diagnostics.CodeAnalysis;
using Basil.Domain.Shared;

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
		if (!TryNormalizePath(path, out var canonical))
			throw new ArgumentException("The path must be a canonical relative asset path.", nameof(path));
		Value = canonical;
	}

	public string Value { get; }

	public bool IsStored => Value is not null && !IsAddress(Value);

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
			return IsAddress(s)
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

	public static bool IsValidPath([NotNullWhen(true)] string? path)
	{
		return path is not null && TryNormalizePath(path, out var canonical) && canonical == path;
	}

	public override string ToString()
	{
		return Value ?? string.Empty;
	}

	public static explicit operator ContentReference(string text)
	{
		return Parse(text, null);
	}

	public static explicit operator ContentReference(Uri address)
	{
		return new ContentReference(address);
	}

	public static explicit operator string(ContentReference reference)
	{
		return reference.ToString();
	}

	private static bool IsAddress(string s)
	{
		return Uri.TryCreate(s, UriKind.Absolute, out var u) && u.IsHttp();
	}

	private static bool TryNormalizePath(string path, [NotNullWhen(true)] out string? canonical)
	{
		canonical = null;
		var trimmed = path.Trim();
		if (trimmed.Length == 0) return false;
		if (trimmed.Any(c => c == '\0' || char.IsControl(c))) return false;
		var text = trimmed.Replace('\\', '/');
		if (text.StartsWith('/') || text.Contains(':')) return false;

		var segments = text.Split('/');
		var parts = new List<string>(segments.Length);
		foreach (var segment in segments)
		{
			if (segment.Length == 0 || segment == ".") continue;
			if (segment == "..") return false;
			if (!IsValidSegment(segment)) return false;
			parts.Add(segment);
		}

		if (parts.Count == 0) return false;

		canonical = string.Join('/', parts);
		return true;
	}

	private static bool IsValidSegment(string segment)
	{
		if (segment.EndsWith('.') || segment.EndsWith(' ')) return false;
		if (segment.Any(c => c is '<' or '>' or ':' or '"' or '|' or '?' or '*' || char.IsControl(c))) return false;

		var dot = segment.IndexOf('.');
		return !IsWindowsDeviceName(dot < 0 ? segment : segment[..dot]);
	}

	private static bool IsWindowsDeviceName(string name)
	{
		if (name.Length == 0) return false;

		if (name.Length == 3)
			return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
			       || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
			       || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
			       || name.Equals("NUL", StringComparison.OrdinalIgnoreCase);

		if (name.Length != 4) return false;
		if (!name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
		    && !name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) return false;

		return name[3] is >= '1' and <= '9';
	}
}