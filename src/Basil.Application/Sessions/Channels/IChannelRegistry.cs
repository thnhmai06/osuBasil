using Basil.Application.Abstractions.Channels;

namespace Basil.Application.Sessions.Channels;

/// <summary>
///     Runtime channel registry (DB metadata + live membership), seeded from IChannelRepository at
///     startup. Ported from app/state/sessions.py's Channels collection.
/// </summary>
public interface IChannelRegistry
{
    IReadOnlyList<ChannelSession> AutoJoinChannels { get; }

    IReadOnlyList<ChannelSession> All { get; }
    void Seed(IReadOnlyList<Channel> channels);

    /// <summary>
    ///     Ported from Channels.append for `instance=True` channels (e.g. `#spec_{hostId}`) — created/removed at runtime,
    ///     not DB-backed.
    /// </summary>
    void Add(ChannelSession channel);

    /// <summary>Ported from Channels.remove for instance channels, called once the last member leaves.</summary>
    void Remove(string name);

    ChannelSession? GetByName(string name);
}