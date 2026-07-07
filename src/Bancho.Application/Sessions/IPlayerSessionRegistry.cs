using Bancho.Domain.Users;
namespace Bancho.Application.Sessions;

/// <summary>
/// Registry of currently online players. Ported from app/state/sessions.py's Players collection,
/// scoped to what login + basic packet dispatch need.
/// </summary>
public interface IPlayerSessionRegistry
{
    void Add(PlayerSession session);

    void Remove(PlayerSession session);

    PlayerSession? GetByToken(string token);

    PlayerSession? GetById(int id);

    /// <summary>Looks up by SafeName.Make(name), matching bancho.py's safe_name comparison.</summary>
    PlayerSession? GetByName(string name);

    IReadOnlyList<PlayerSession> All { get; }
}
