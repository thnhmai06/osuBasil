using System.Security.Cryptography;

namespace Basil.Domain.Utilities;

public readonly record struct Md5
{
	public string HashValue { get; }

	public Md5(string hash)
	{
		ArgumentNullException.ThrowIfNull(hash);
		if (hash is not { Length: 32 }
		    || !hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
			throw new ArgumentException("The MD5 value is invalid.", nameof(hash));

		HashValue = hash.ToLowerInvariant();
	}

	public Md5(byte[] value)
	{
		HashValue = Convert.ToHexStringLower(MD5.HashData(value));
	}

	public override string ToString()
	{
		return HashValue;
	}

	public static implicit operator Md5(string hash)
	{
		return new Md5(hash);
	}
}