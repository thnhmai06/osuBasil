namespace Basil.Domain.Client;

/// <summary>What an osu! client reports about itself.</summary>
/// <param name="Version">The client build.</param>
/// <param name="Fingerprint">The identifying hashes the client reported about its installation.</param>
public sealed record ClientBuild(OsuVersion Version, ClientFingerprint Fingerprint);