using System.Security.Cryptography;

namespace Basil.Domain.Cryptography;

public readonly record struct Md5 : IParsable<Md5>
{
	public Md5(string hash)
	{
		ArgumentNullException.ThrowIfNull(hash);
		if (!IsHex(hash)) throw new ArgumentException("The MD5 value is invalid.", nameof(hash));

		HashValue = hash.ToLowerInvariant();
	}

	public Md5(byte[] value)
	{
		HashValue = Convert.ToHexStringLower(MD5.HashData(value));
	}

	public string HashValue { get; }

	public static Md5 Parse(string s, IFormatProvider? provider = null)
	{
		return TryParse(s, provider, out var result) ? result : throw new FormatException("The MD5 value is invalid.");
	}

	public static bool TryParse(string? s, IFormatProvider? provider, out Md5 result)
	{
		if (s is not null && IsHex(s))
		{
			result = new Md5(s);
			return true;
		}

		result = default;
		return false;
	}

	public static async Task<Md5> ComputeAsync(Stream content, CancellationToken ct = default)
	{
		var hash = await MD5.HashDataAsync(content, ct);
		return new Md5(Convert.ToHexStringLower(hash));
	}

	public override string ToString()
	{
		return HashValue;
	}

	private static bool IsHex(string hash)
	{
		return hash is { Length: 32 } &&
		       hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
	}

	public static implicit operator Md5(string hash)
	{
		return new Md5(hash);
	}
}