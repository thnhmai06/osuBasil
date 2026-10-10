using Basil.Domain.Utilities;

namespace Basil.Domain.Client;

/// <summary>
///     Represents the client fingerprint captured from an osu! client at login.
/// </summary>
/// <remarks>
///     The fingerprint is the osu! client hash: five components identifying the
///     installation (and, after re-installation, the machine) a player is using.
/// </remarks>
public readonly record struct ClientFingerprint(
	Md5 OsuPathHash,
	NetworkAdapters NetworkAdapters,
	Md5 UninstallHash,
	Md5 DiskSignatureHash);
