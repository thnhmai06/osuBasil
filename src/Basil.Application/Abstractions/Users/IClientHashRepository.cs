namespace Basil.Application.Abstractions.Users;

/// <summary>Ported from app/repositories/client_hashes.py's ClientHash dataclass.</summary>
public sealed record ClientHash(
    int UserId,
    string OsuPath,
    string Adapters,
    string UninstallId,
    string DiskSerial,
    DateTime LatestTime,
    int Occurrences);

/// <summary>Ported from app/repositories/client_hashes.py's ClientHashWithPlayer dataclass.</summary>
public sealed record ClientHashWithPlayer(
    int UserId,
    string OsuPath,
    string Adapters,
    string UninstallId,
    string DiskSerial,
    DateTime LatestTime,
    int Occurrences,
    string Name,
    int Priv);

/// <summary>
///     Ported from app/repositories/client_hashes.py's ClientHashesRepository, scoped to what login
///     needs: recording a hash entry (upsert bumping occurrences) and the hardware-ban lookup.
/// </summary>
public interface IClientHashRepository
{
    Task<ClientHash> CreateAsync(int userId, string osuPath, string adapters, string uninstallId, string diskSerial,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Finds other users (not <paramref name="userId" />) sharing hardware identifiers. When
    ///     <paramref name="runningUnderWine" />, only <paramref name="uninstallId" /> is compared
    ///     (adapters/disk serial are unreliable under Wine); otherwise any of adapters, uninstallId,
    ///     or diskSerial matching is sufficient.
    /// </summary>
    Task<IReadOnlyList<ClientHashWithPlayer>> FetchAnyHardwareMatchesForUserAsync(
        int userId,
        bool runningUnderWine,
        string adapters,
        string uninstallId,
        string? diskSerial,
        CancellationToken cancellationToken = default);
}