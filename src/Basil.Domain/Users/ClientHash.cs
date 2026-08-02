namespace Basil.Domain.Users;

/// <summary>
///     A hardware identity fingerprint recorded against a user, as stored in the ClientHashes table.
/// </summary>
/// <param name="UserId">The id of the user the fingerprint belongs to.</param>
/// <param name="OsuPathMd5">The md5 of the client's osu! installation path.</param>
/// <param name="Adapters">A fingerprint of the machine's network adapters.</param>
/// <param name="UninstallId">The client's uninstall id, a per-installation GUID.</param>
/// <param name="DiskSerial">A fingerprint of the primary disk's serial number.</param>
/// <param name="LastSeenAt">The last time a login produced this exact fingerprint, in UTC.</param>
/// <param name="Occurrences">The number of logins recorded against this exact fingerprint.</param>
public record ClientHash(
	int UserId,
	string OsuPathMd5,
	string Adapters,
	string UninstallId,
	string DiskSerial,
	DateTime LastSeenAt,
	int Occurrences);

/// <summary>
///     A hardware fingerprint alongside the user it belongs to, used by the shared-hardware check.
/// </summary>
/// <param name="UserId">The id of the user the fingerprint belongs to.</param>
/// <param name="OsuPathMd5">The md5 of the client's osu! installation path.</param>
/// <param name="Adapters">A fingerprint of the machine's network adapters.</param>
/// <param name="UninstallId">The client's uninstall id, a per-installation GUID.</param>
/// <param name="DiskSerial">A fingerprint of the primary disk's serial number.</param>
/// <param name="LastSeenAt">The last time a login produced this exact fingerprint, in UTC.</param>
/// <param name="Occurrences">The number of logins recorded against this exact fingerprint.</param>
/// <param name="Name">The name of the user.</param>
/// <param name="Privilege">The server-side privileges granted to the user.</param>
public sealed record PlayerClientHash(
	int UserId,
	string OsuPathMd5,
	string Adapters,
	string UninstallId,
	string DiskSerial,
	DateTime LastSeenAt,
	int Occurrences,
	string Name,
	UserPrivileges Privilege)
	: ClientHash(UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial, LastSeenAt, Occurrences);