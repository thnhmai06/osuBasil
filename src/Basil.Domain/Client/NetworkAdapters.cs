using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Basil.Domain.Client;

public readonly partial struct NetworkAdapters : IParsable<NetworkAdapters>, IEquatable<NetworkAdapters>, IFormattable
{
	private const string WineAdapterSentinel = "runningunderwine";

	public bool IsRunningUnderWine { get; private init; }

	public string Md5 { get; private init; }

	public NetworkAdapters(string adaptersString, string adapterMd5)
	{
		if (!adaptersString.EndsWith('.'))
			throw new FormatException("Adapter string is missing trailing delimiter.");

		if (!Md5Pattern().IsMatch(adapterMd5))
			throw new FormatException("Adapter MD5 is invalid.");

		IsRunningUnderWine = adaptersString[..^1]
			.Split('.', StringSplitOptions.RemoveEmptyEntries)
			.Any(a => a.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase));
		Md5 = adapterMd5;
	}

	public static NetworkAdapters Parse(string hash, IFormatProvider? provider = null)
	{
		if (string.IsNullOrEmpty(hash))
			throw new FormatException("Hash cannot be empty.");
		hash = hash.ToLowerInvariant();

		// [n|w][32-char MD5]
		var wineChar = hash[0];
		if (hash.Length != 33 || wineChar is not 'n' and 'w')
			throw new FormatException("Hash is invalid.");

		var md5 = hash[1..];
		if (!Md5Pattern().IsMatch(md5))
			throw new FormatException("Hash has invalid MD5.");

		return new NetworkAdapters
		{
			IsRunningUnderWine = wineChar == 'w',
			Md5 = md5
		};
	}

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out NetworkAdapters result)
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

	public string ToString(string? format, IFormatProvider? formatProvider)
	{
		var wineChar = IsRunningUnderWine ? 'w' : 'n';
		return wineChar + Md5;
	}

	public override string ToString()
	{
		return ToString(null, null);
	}

	public bool Equals(NetworkAdapters other)
	{
		return Md5 == other.Md5;
	}

	public override bool Equals(object? obj)
	{
		return obj is NetworkAdapters adapters && Equals(adapters);
	}

	public override int GetHashCode()
	{
		return Md5.GetHashCode();
	}

	public static bool operator ==(NetworkAdapters left, NetworkAdapters right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(NetworkAdapters left, NetworkAdapters right)
	{
		return !(left == right);
	}

	[GeneratedRegex("^[a-fA-F0-9]{32}$")]
	private static partial Regex Md5Pattern();
}