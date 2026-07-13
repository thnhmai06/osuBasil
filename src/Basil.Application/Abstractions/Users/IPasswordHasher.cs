namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Verifies/creates password hashes. Ported from app/services/bancho.py's
///     BanchoAuthenticationService: bancho.py stores bcrypt hashes of the md5 digest of the
///     plaintext password (the osu! client sends md5(password) at login, never the raw password).
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    ///     Bcrypt-hashes a password's md5 digest, for storing on registration. Takes the UTF-8 bytes
    ///     of the digest's 32-character lowercase-hex string — NOT the raw 16-byte digest — matching
    ///     what the osu! client sends and what <see cref="Verify" /> expects.
    /// </summary>
    string Hash(byte[] passwordMd5Hex);

    /// <summary>
    ///     Verifies an untrusted md5 password digest against a trusted stored bcrypt hash. Takes the
    ///     UTF-8 bytes of the digest's 32-character lowercase-hex string, same as <see cref="Hash" />.
    ///     Caches verified (hash -> md5) pairs in memory so repeat logins skip the ~200ms bcrypt check.
    /// </summary>
    bool Verify(byte[] untrustedPasswordMd5Hex, string trustedBcryptHash);
}