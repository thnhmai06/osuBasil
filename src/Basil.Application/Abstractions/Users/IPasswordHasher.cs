namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Verifies/creates password hashes. Ported from app/services/bancho.py's
///     BanchoAuthenticationService: bancho.py stores bcrypt hashes of the md5 digest of the
///     plaintext password (the osu! client sends md5(password) at login, never the raw password).
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Bcrypt-hashes an md5 password digest, for storing on registration.</summary>
    string Hash(byte[] passwordMd5);

    /// <summary>
    ///     Verifies an untrusted md5 password digest against a trusted stored bcrypt hash. Caches
    ///     verified (hash -> md5) pairs in memory so repeat logins skip the ~200ms bcrypt check.
    /// </summary>
    bool Verify(byte[] untrustedPasswordMd5, string trustedBcryptHash);
}