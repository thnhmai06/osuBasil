using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Basil.Domain.Client;

public readonly partial record struct NetworkAdapters : IParsable<NetworkAdapters>
{
	private const string WineAdapterSentinel = "runningunderwine";

	public string Adapters { get; }

	public string Md5 { get; }

	public bool IsRunningUnderWine => Adapters.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase);

	public NetworkAdapters(string adapters, string md5)
	{
		if (string.IsNullOrEmpty(adapters))
			throw new FormatException("Adapter string cannot be empty.");

		if (!IsValidAdaptersString(adapters))
			throw new FormatException("Adapter string is invalid.");

		if (!Md5Pattern().IsMatch(md5))
			throw new FormatException("Adapter MD5 is invalid.");

		Adapters = adapters;
		Md5 = md5.ToLowerInvariant();
	}

	public static NetworkAdapters Parse(string s, IFormatProvider? provider = null)
	{
		var parts = s.Split(':', 2);

		return parts.Length == 2
			? new NetworkAdapters(parts[0], parts[1])
			: throw new FormatException("Network adapters value must contain 2 components.");
	}

	public static bool TryParse(
		[NotNullWhen(true)] string? s,
		IFormatProvider? provider,
		out NetworkAdapters result)
	{
		if (s is null)
		{
			result = default;
			return false;
		}

		try
		{
			result = Parse(s, provider);
			return true;
		}
		catch (FormatException)
		{
			result = default;
			return false;
		}
	}

	public override string ToString()
	{
		return $"{Adapters}:{Md5}";
	}

	public bool Equals(NetworkAdapters other)
	{
		return string.Equals(Md5, other.Md5, StringComparison.OrdinalIgnoreCase);
	}

	public override int GetHashCode()
	{
		return StringComparer.OrdinalIgnoreCase.GetHashCode(Md5);
	}

	[GeneratedRegex("^[a-fA-F0-9]{32}$")]
	private static partial Regex Md5Pattern();

	private static bool IsValidAdaptersString(string value)
	{
		if (value.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase))
			return true;

		return value.EndsWith('.')
		       && value[..^1]
			       .Split('.', StringSplitOptions.RemoveEmptyEntries)
			       .All(a => !a.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase));
	}
}