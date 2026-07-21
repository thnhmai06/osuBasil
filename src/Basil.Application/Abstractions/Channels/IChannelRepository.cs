using Basil.Domain.Users;

namespace Basil.Application.Abstractions.Channels;

/// <summary>Ported from app/repositories/channels.py's Channel dataclass.</summary>
public sealed record Channel(int Id, string Name, string Topic, UserPrivileges ReadPriv, UserPrivileges WritePriv, bool AutoJoin);

/// <summary>
///     Ported from app/repositories/channels.py's ChannelsRepository, scoped to what login needs:
///     the auto-join channel list sent at login.
/// </summary>
public interface IChannelRepository
{
    Task<IReadOnlyList<Channel>> FetchAllAutoJoinAsync(CancellationToken cancellationToken = default);

    Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default);
}