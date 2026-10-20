using System.Security.Cryptography;

namespace Basil.Domain.Common;

/// <summary>An MD5 digest, kept as 32 lower-case hexadecimal characters.</summary>
public readonly record struct Md5
{
	/// <summary>Creates a digest from its hexadecimal text.</summary>
	/// <param name="hash">The 32 hexadecimal characters, in either case.</param>
	/// <exception cref="ArgumentException">The text is not 32 hexadecimal characters.</exception>
	public Md5(string hash)
	{
		ArgumentNullException.ThrowIfNull(hash);
		if (hash is not { Length: 32 }
		    || !hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
			throw new ArgumentException("The MD5 value is invalid.", nameof(hash));

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
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The hash of the content.</returns>
	public static async Task<Md5> ComputeAsync(Stream content, CancellationToken cancellationToken = default)
	{
		var hash = await MD5.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
		return new Md5(Convert.ToHexStringLower(hash));
	}

	/// <summary>Returns the digest as 32 lower-case hexadecimal characters.</summary>
	/// <returns>The digest text.</returns>
	public override string ToString()
	{
		return HashValue;
	}

	/// <summary>Creates a digest from its hexadecimal text.</summary>
	/// <param name="hash">The 32 hexadecimal characters, in either case.</param>
	public static implicit operator Md5(string hash)
	{
		return new Md5(hash);
	}
}