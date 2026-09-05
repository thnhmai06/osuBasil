using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Users;
using BC = BCrypt.Net.BCrypt;

namespace Basil.Infrastructure.Security;

/// <inheritdoc cref="IPasswordHasher" />
/// <remarks>
///     Both inputs are the UTF-8 bytes of the secret being hashed (a password's md5 digest, or the
///     server's admin key). Successful verifications are cached per stored hash, so repeat calls
///     for the same stored hash compare the candidate's digest against the cached one in constant
///     time, without re-running the hash.
/// </remarks>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
	/// <summary>
	///     Caches a SHA-256 digest of the secret that verified against each stored hash, keyed by the
	///     stored hash string. The secret itself is never retained: the digest is one-way, so a
	///     compromise of this cache alone cannot recover it.
	/// </summary>
	private readonly ConcurrentDictionary<string, byte[]> _cache = new();

	/// <inheritdoc />
	public string Hash(byte[] secretBytes)
	{
		return BC.HashPassword(Encoding.UTF8.GetString(secretBytes));
	}

	/// <inheritdoc />
	/// <remarks>
	///     On the first successful verification for a stored hash, a digest of the candidate is
	///     cached against it; later calls short-circuit on <see cref="_cache" /> before re-verifying,
	///     comparing digests in constant time so a cache hit cannot leak timing information about
	///     the secret. Failed verifications never populate the cache.
	/// </remarks>
	public bool Verify(byte[] untrustedSecretBytes, string trustedBcryptHash)
	{
		var candidateDigest = SHA256.HashData(untrustedSecretBytes);

		if (_cache.TryGetValue(trustedBcryptHash, out var cachedDigest))
			return CryptographicOperations.FixedTimeEquals(cachedDigest, candidateDigest);

		if (!BC.Verify(Encoding.UTF8.GetString(untrustedSecretBytes), trustedBcryptHash)) return false;

		_cache[trustedBcryptHash] = candidateDigest;
		return true;
	}
}