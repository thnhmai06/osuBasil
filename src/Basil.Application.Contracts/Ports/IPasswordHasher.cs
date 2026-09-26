namespace Basil.Application.Contracts.Ports;

/// <summary>
///     Creates and verifies password hashes for any secret this server stores instead of plaintext:
///     user passwords (as their MD5 digest, matching what the osu! client sends at login) and the
///     server's own admin key.
/// </summary>
public interface IPasswordHasher
{
	/// <summary>Hashes the given secret bytes, for storing at rest.</summary>
	/// <param name="secretBytes">The UTF-8 bytes of the secret to hash.</param>
	/// <returns>A hash string suitable for storage.</returns>
	string Hash(byte[] secretBytes);

	/// <summary>Verifies an untrusted secret against a trusted stored hash.</summary>
	/// <param name="untrustedSecretBytes">The UTF-8 bytes of the secret to verify, same form as <see cref="Hash" />.</param>
	/// <param name="trustedHash">The stored hash to check against.</param>
	/// <returns><see langword="true" /> if the secret matches the hash; otherwise, <see langword="false" />.</returns>
	bool Verify(byte[] untrustedSecretBytes, string trustedHash);
}