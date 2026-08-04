using System.Collections.Concurrent;
using System.Text;
using Basil.Application.Abstractions.Users;
using BC = BCrypt.Net.BCrypt;

namespace Basil.Infrastructure.Security;

/// <inheritdoc cref="IPasswordHasher" />
/// <remarks>
///     Uses BCrypt.Net to hash and verify. Both inputs are the UTF-8 bytes of the secret being
///     hashed (a password's md5 digest, or the server's admin key). Successful verifications are
///     cached per stored hash, so the expensive bcrypt work runs only once per secret per process;
///     repeat calls for the same stored hash compare the candidate bytes directly against the
///     cached match.
/// </remarks>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
	/// <summary>
	///     Caches the secret bytes that verified against each stored bcrypt hash, keyed by the
	///     stored hash string.
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
	///     against it; later calls short-circuit on <see cref="_cache" /> before invoking bcrypt.
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