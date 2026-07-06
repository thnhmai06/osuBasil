using Bancho.Application.Abstractions;

namespace Bancho.Application.Sessions;

/// <summary>
/// Runtime channel registry (DB metadata + live membership), seeded from IChannelRepository at
/// startup. Ported from app/state/sessions.py's Channels collection.
/// </summary>
public interface IChannelRegistry
{
    void Seed(IReadOnlyList<Channel> channels);

    ChannelSession? GetByName(string name);

    IReadOnlyList<ChannelSession> AutoJoinChannels { get; }

    IReadOnlyList<ChannelSession> All { get; }
}
