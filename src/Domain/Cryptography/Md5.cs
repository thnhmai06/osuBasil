using System.Security.Cryptography;

namespace Basil.Domain.Cryptography;

/// <summary>An MD5 digest, kept as 32 lower-case hexadecimal characters.</summary>
public readonly record struct Md5 : IParsable<Md5>
{
	/// <summary>Creates a digest from its hexadecimal text.</summary>
	/// <param name="hash">The 32 hexadecimal characters, in either case.</param>
	/// <exception cref="ArgumentException">The text is not 32 hexadecimal characters.</exception>
	public Md5(string hash)
	{
		ArgumentNullException.ThrowIfNull(hash);
		if (!IsHex(hash)) throw new ArgumentException("The MD5 value is invalid.", nameof(hash));

		HashValue = hash.ToLowerInvariant();
	}

	/// <summary>Creates the digest of some content.</summary>
	/// <param name="value">The content to hash.</param>
	public Md5(byte[] value)
	{
		HashValue = Convert.ToHexStringLower(MD5.HashData(value));
	}

	/// <summary>Gets the digest as 32 lower-case hexadecimal characters.</summary>
	public string HashValue { get; }

	/// <summary>Computes the MD5 hash of a content read to its end.</summary>
	/// <param name="content">The content, read from its current position; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The hash of the content.</returns>
	public static async Task<Md5> ComputeAsync(Stream content, CancellationToken ct = default)
	{
		var hash = await MD5.HashDataAsync(content, ct);
		return new Md5(Convert.ToHexStringLower(hash));
	}

	/// <summary>Returns the digest as 32 lower-case hexadecimal characters.</summary>
	/// <returns>The digest text.</returns>
	public override string ToString()
	{
		return HashValue;
	}

	/// <summary>Reads a digest from its hexadecimal text.</summary>
	/// <param name="s">The 32 hexadecimal characters, in either case.</param>
	/// <param name="provider">Ignored.</param>
	/// <returns>The digest.</returns>
	/// <exception cref="FormatException">The text is not 32 hexadecimal characters.</exception>
	public static Md5 Parse(string s, IFormatProvider? provider = null)
	{
		return TryParse(s, provider, out var result) ? result : throw new FormatException("The MD5 value is invalid.");
	}

	/// <summary>Reads a digest from its hexadecimal text without throwing.</summary>
	/// <param name="s">The text to read.</param>
	/// <param name="provider">Ignored.</param>
	/// <param name="result">The digest, or the default value when the text is not 32 hexadecimal characters.</param>
	/// <returns><see langword="true" /> when the text is a digest.</returns>
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

	private static bool IsHex(string hash)
	{
		return hash is { Length: 32 } && hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
	}

	/// <summary>Creates a digest from its hexadecimal text.</summary>
	/// <param name="hash">The 32 hexadecimal characters, in either case.</param>
	public static implicit operator Md5(string hash)
	{
		return new Md5(hash);
	}
}