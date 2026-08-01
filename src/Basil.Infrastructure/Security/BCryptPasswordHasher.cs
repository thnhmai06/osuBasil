using System.Collections.Concurrent;
using System.Text;
using Basil.Application.Abstractions.Users;
using BC = BCrypt.Net.BCrypt;

namespace Basil.Infrastructure.Security;

/// <inheritdoc cref="IPasswordHasher" />
/// <remarks>
///     Uses BCrypt.Net to hash and verify. Both inputs are the UTF-8 bytes of a password md5
///     digest's 32-character lowercase-hex string. Successful verifications are cached per stored
///     hash, so the expensive bcrypt work runs only once per account per process; repeat logins for
///     the same account compare the candidate bytes directly against the cached match.
/// </remarks>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
	/// <summary>
	///     Caches the password md5 bytes that verified against each stored bcrypt hash, keyed by the
	///     stored hash string.
	/// </summary>
	private readonly ConcurrentDictionary<string, byte[]> _cache = new();

	/// <inheritdoc />
	public string Hash(byte[] passwordMd5Hex)
	{
		return BC.HashPassword(Encoding.UTF8.GetString(passwordMd5Hex));
	}

	/// <inheritdoc />
	/// <remarks>
	///     On the first successful verification for a stored hash, the candidate bytes are cached
	///     against it; later calls short-circuit on <see cref="_cache" /> before invoking bcrypt.
	///     Failed verifications never populate the cache.
	/// </remarks>
	public bool Verify(byte[] untrustedPasswordMd5Hex, string trustedBcryptHash)
	{
		if (_cache.TryGetValue(trustedBcryptHash, out var cachedMd5))
			return cachedMd5.AsSpan().SequenceEqual(untrustedPasswordMd5Hex);

		if (!BC.Verify(Encoding.UTF8.GetString(untrustedPasswordMd5Hex), trustedBcryptHash)) return false;

		_cache[trustedBcryptHash] = untrustedPasswordMd5Hex;
		return true;
	}
}