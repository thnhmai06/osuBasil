namespace Basil.Domain.Bancho;

/// <summary>The osu! client version and fingerprint reported at login.</summary>
/// <param name="Version">The client version.</param>
/// <param name="Fingerprint">The client fingerprint.</param>
public sealed record BanchoClient(BanchoVersion Version, BanchoFingerprint Fingerprint);