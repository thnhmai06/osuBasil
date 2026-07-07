using System.Collections.Concurrent;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain.Users;

namespace OpenOsuTournament.Bancho.Infrastructure.Sessions;

/// <inheritdoc cref="IPlayerSessionRegistry" />
public sealed class InMemoryPlayerSessionRegistry : IPlayerSessionRegistry
{
    private readonly ConcurrentDictionary<string, PlayerSession> _byToken = new();

    public void Add(PlayerSession session)
    {
        _byToken[session.Token] = session;
    }

    public void Remove(PlayerSession session)
    {
        _byToken.TryRemove(session.Token, out _);
    }

    public PlayerSession? GetByToken(string token)
    {
        return _byToken.GetValueOrDefault(token);
    }

    public PlayerSession? GetById(int id)
    {
        return _byToken.Values.FirstOrDefault(s => s.Id == id);
    }

    public PlayerSession? GetByName(string name)
    {
        var safeName = SafeName.Make(name);
        return _byToken.Values.FirstOrDefault(s => s.SafeName == safeName);
    }

    public IReadOnlyList<PlayerSession> All => _byToken.Values.ToList();
}