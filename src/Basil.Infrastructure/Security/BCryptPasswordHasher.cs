using System.Collections.Concurrent;
using System.Text;
using Basil.Application.Abstractions.Users;
using BC = BCrypt.Net.BCrypt;

namespace Basil.Infrastructure.Security;

/// <inheritdoc cref="IPasswordHasher" />
/// <remarks>
///     Both inputs are the UTF-8 bytes of the secret being hashed (a password's md5 digest, or the
///     server's admin key). Successful verifications are cached per stored hash, so repeat calls
///     for the same stored hash compare the candidate bytes directly against the cached match
///     without re-running the hash.
/// </remarks>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
	/// <summary>
	///     Caches the secret bytes that verified against each stored hash, keyed by the stored hash
	///     string.
	/// </summary>
	private readonly ConcurrentDictionary<string, byte[]> _cache = new();

	/// <inheritdoc />
	public string Hash(byte[] secretBytes)
	{
		return BC.HashPassword(Encoding.UTF8.GetString(secretBytes));
	}

	/// <inheritdoc />
	/// <remarks>
	///     On the first successful verification for a stored hash, the candidate bytes are cached
	///     against it; later calls short-circuit on <see cref="_cache" /> before re-verifying.
	///     Failed verifications never populate the cache.
	/// </remarks>
	public bool Verify(byte[] untrustedSecretBytes, string trustedBcryptHash)
	{
		if (_cache.TryGetValue(trustedBcryptHash, out var cachedSecret))
			return cachedSecret.AsSpan().SequenceEqual(untrustedSecretBytes);

		if (!BC.Verify(Encoding.UTF8.GetString(untrustedSecretBytes), trustedBcryptHash)) return false;

		_cache[trustedBcryptHash] = untrustedSecretBytes;
		return true;
	}
}