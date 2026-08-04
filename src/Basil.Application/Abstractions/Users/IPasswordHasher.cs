namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Creates and verifies bcrypt hashes for any secret this server stores instead of plaintext:
///     user passwords (as their md5 digest, matching what the osu! client sends at login) and the
///     server's own admin key.
/// </summary>
/// <remarks>
///     Bcrypt truncates its input at 72 bytes; callers hashing an arbitrary-length secret (unlike a
///     fixed 32-character md5 digest) must enforce their own length limit before calling
///     <see cref="Hash" />, or two different secrets sharing a 72-byte prefix will verify as equal.
/// </remarks>
public interface IPasswordHasher
{
	/// <summary>Creates a bcrypt hash of the given secret bytes, for storing at rest.</summary>
	/// <param name="secretBytes">The UTF-8 bytes of the secret to hash.</param>
	/// <returns>A bcrypt hash string suitable for storage.</returns>
	string Hash(byte[] secretBytes);

	/// <summary>Verifies an untrusted secret against a trusted stored bcrypt hash.</summary>
	/// <param name="untrustedSecretBytes">The UTF-8 bytes of the secret to verify, same form as <see cref="Hash" />.</param>
	/// <param name="trustedBcryptHash">The stored bcrypt hash to check against.</param>
	/// <returns>
	///     <see langword="true" /> if the secret matches the hash; otherwise, <see langword="false" />.
	/// </returns>
	/// <remarks>
	///     Callers invoke this on every login/authentication attempt, so a repeat verification of the
	///     same account or key is expected and cheap.
	/// </remarks>
	bool Verify(byte[] untrustedSecretBytes, string trustedBcryptHash);
}