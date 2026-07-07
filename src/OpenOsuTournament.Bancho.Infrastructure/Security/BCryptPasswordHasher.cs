using System.Collections.Concurrent;
using System.Text;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;
using BC = BCrypt.Net.BCrypt;

namespace OpenOsuTournament.Bancho.Infrastructure.Security;

/// <inheritdoc cref="IPasswordHasher" />
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    public string Hash(byte[] passwordMd5)
    {
        return BC.HashPassword(Encoding.UTF8.GetString(passwordMd5));
    }

    public bool Verify(byte[] untrustedPasswordMd5, string trustedBcryptHash)
    {
        if (_cache.TryGetValue(trustedBcryptHash, out var cachedMd5))
            return cachedMd5.AsSpan().SequenceEqual(untrustedPasswordMd5);

        if (!BC.Verify(Encoding.UTF8.GetString(untrustedPasswordMd5), trustedBcryptHash)) return false;

        _cache[trustedBcryptHash] = untrustedPasswordMd5;
        return true;
    }
}