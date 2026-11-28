using System.Diagnostics.CodeAnalysis;

namespace Basil.Shared.Utilities;

public static class PathValidation
{
	public static bool IsValidPath([NotNullWhen(true)] string? path)
	{
		return path is not null && TryNormalizePath(path, out var canonical) && canonical == path;
	}

	public static bool TryNormalizePath(string path, [NotNullWhen(true)] out string? canonical)
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

		static bool IsValidSegment(string segment)
		{
			if (segment.EndsWith('.') || segment.EndsWith(' ')) return false;
			if (segment.Any(c => c is '<' or '>' or ':' or '"' or '|' or '?' or '*' || char.IsControl(c))) return false;

			var dot = segment.IndexOf('.');
			return !IsWindowsDeviceName(dot < 0 ? segment : segment[..dot]);
		}

		static bool IsWindowsDeviceName(string name)
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
}