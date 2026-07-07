using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;

namespace OpenOsuTournament.Bancho.Infrastructure.Sessions;

/// <inheritdoc cref="IMatchRegistry" />
public sealed class InMemoryMatchRegistry : IMatchRegistry
{
    private const int MaxMatches = 64;
    private readonly object _registryLock = new();

    private readonly MatchSession?[] _slots = new MatchSession?[MaxMatches];

    public MatchSession? GetById(int id)
    {
        if (id < 0 || id >= MaxMatches) return null;

        lock (_registryLock)
        {
            return _slots[id];
        }
    }

    public MatchSession? TryCreate(Func<int, MatchSession> factory)
    {
        lock (_registryLock)
        {
            for (var i = 0; i < MaxMatches; i++)
                if (_slots[i] is null)
                {
                    var match = factory(i);
                    _slots[i] = match;
                    return match;
                }

            return null;
        }
    }

    public void Remove(int id)
    {
        if (id < 0 || id >= MaxMatches) return;

        lock (_registryLock)
        {
            _slots[id] = null;
        }
    }

    public IReadOnlyList<MatchSession> All
    {
        get
        {
            lock (_registryLock)
            {
                return _slots.Where(m => m is not null).Cast<MatchSession>().ToList();
            }
        }
    }
}