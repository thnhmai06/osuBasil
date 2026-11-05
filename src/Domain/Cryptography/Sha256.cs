using System.Security.Cryptography;

namespace Basil.Domain.Cryptography;

/// <summary>A SHA-256 digest, kept as 64 lower-case hexadecimal characters.</summary>
public readonly record struct Sha256
{
    /// <summary>Creates a digest from its hexadecimal text.</summary>
    /// <param name="hash">The 64 hexadecimal characters, in either case.</param>
    /// <exception cref="ArgumentException">The text is not 64 hexadecimal characters.</exception>
    public Sha256(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash is not { Length: 64 }
            || !hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            throw new ArgumentException("The SHA-256 value is invalid.", nameof(hash));

        HashValue = hash.ToLowerInvariant();
    }

    /// <summary>Creates the digest of some content.</summary>
    /// <param name="value">The content to hash.</param>
    public Sha256(byte[] value)
    {
        HashValue = Convert.ToHexStringLower(SHA256.HashData(value));
    }

    /// <summary>Gets the digest as 64 lower-case hexadecimal characters.</summary>
    public string HashValue { get; }

    /// <summary>Computes the SHA-256 hash of a content read to its end.</summary>
    /// <param name="content">The content, read from its current position; not disposed.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The hash of the content.</returns>
    public static async Task<Sha256> ComputeAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var hash = await SHA256.HashDataAsync(content, cancellationToken);
        return new Sha256(Convert.ToHexStringLower(hash));
    }

    /// <summary>Returns the digest as 64 lower-case hexadecimal characters.</summary>
    /// <returns>The digest text.</returns>
    public override string ToString()
    {
        return HashValue;
    }

    /// <summary>Creates a digest from its hexadecimal text.</summary>
    /// <param name="hash">The 64 hexadecimal characters, in either case.</param>
    public static implicit operator Sha256(string hash)
    {
        return new Sha256(hash);
    }
}