namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Creates and verifies the password hashes this server stores.
/// </summary>
/// <remarks>
///     Basil stores bcrypt hashes of the md5 digest of the plaintext password, never the raw
///     password itself. This matches what the osu! client sends at login, which is
///     md5(password), so the raw password never needs to cross the wire or enter these methods.
/// </remarks>
public interface IPasswordHasher
{
	/// <summary>
	///     Creates a bcrypt hash of a password's md5 digest, for storing on registration.
	/// </summary>
	/// <param name="passwordMd5Hex">
	///     The UTF-8 bytes of the digest's 32-character lowercase-hex string, not the raw 16-byte
	///     digest.
	/// </param>
	/// <returns>A bcrypt hash string suitable for storage.</returns>
	/// <remarks>
	///     The input form matches what the osu! client sends and what <see cref="Verify" /> expects.
	/// </remarks>
	string Hash(byte[] passwordMd5Hex);

	/// <summary>
	///     Verifies an untrusted password md5 digest against a trusted stored bcrypt hash.
	/// </summary>
	/// <param name="untrustedPasswordMd5Hex">
	///     The UTF-8 bytes of the digest's 32-character lowercase-hex string, same form as
	///     <see cref="Hash" />.
	/// </param>
	/// <param name="trustedBcryptHash">The stored bcrypt hash to check against.</param>
	/// <returns>
	///     <see langword="true" /> if the digest matches the hash; otherwise, <see langword="false" />.
	/// </returns>
	/// <remarks>
	///     Callers invoke this on every login attempt, so a repeat verification of the same account
	///     is expected and cheap.
	/// </remarks>
	bool Verify(byte[] untrustedPasswordMd5Hex, string trustedBcryptHash);
}