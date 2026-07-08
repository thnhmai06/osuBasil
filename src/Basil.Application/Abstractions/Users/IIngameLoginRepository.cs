namespace Basil.Application.Abstractions.Users;

/// <summary>Ported from app/repositories/ingame_logins.py's IngameLogin dataclass.</summary>
public sealed record IngameLogin(int Id, int UserId, string Ip, DateOnly OsuVer, string OsuStream, DateTime Datetime);

/// <summary>
///     Ported from app/repositories/ingame_logins.py's IngameLoginsRepository, scoped to what login
///     needs: recording a login entry.
/// </summary>
public interface IIngameLoginRepository
{
    Task<IngameLogin> CreateAsync(int userId, string ip, DateOnly osuVer, string osuStream,
        CancellationToken cancellationToken = default);
}