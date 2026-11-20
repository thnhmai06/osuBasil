using System.Security.Cryptography;

namespace Basil.Shared.Structures.Cryptography;

public readonly record struct Sha256Hash
{
	public Sha256Hash(string hash)
	{
		ArgumentNullException.ThrowIfNull(hash);
		if (hash is not { Length: 64 }
		    || !hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
			throw new ArgumentException("The SHA-256 value is invalid.", nameof(hash));

		HashValue = hash.ToLowerInvariant();
	}

	public Sha256Hash(byte[] value)
	{
		HashValue = Convert.ToHexStringLower(SHA256.HashData(value));
	}

	public string HashValue { get; }

	public static async Task<Sha256Hash> ComputeAsync(Stream content, CancellationToken ct = default)
	{
		var hash = await SHA256.HashDataAsync(content, ct);
		return new Sha256Hash(Convert.ToHexStringLower(hash));
	}

	public override string ToString()
	{
		return HashValue;
	}
}