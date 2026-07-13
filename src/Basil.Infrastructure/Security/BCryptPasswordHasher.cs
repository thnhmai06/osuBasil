using System.Collections.Concurrent;
using System.Text;
using Basil.Application.Abstractions.Users;
using BC = BCrypt.Net.BCrypt;

namespace Basil.Infrastructure.Security;

/// <inheritdoc cref="IPasswordHasher" />
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    public string Hash(byte[] passwordMd5Hex)
    {
        return BC.HashPassword(Encoding.UTF8.GetString(passwordMd5Hex));
    }

    public bool Verify(byte[] untrustedPasswordMd5Hex, string trustedBcryptHash)
    {
        if (_cache.TryGetValue(trustedBcryptHash, out var cachedMd5))
            return cachedMd5.AsSpan().SequenceEqual(untrustedPasswordMd5Hex);

        if (!BC.Verify(Encoding.UTF8.GetString(untrustedPasswordMd5Hex), trustedBcryptHash)) return false;

        _cache[trustedBcryptHash] = untrustedPasswordMd5Hex;
        return true;
    }
}