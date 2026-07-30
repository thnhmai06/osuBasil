using Basil.Domain.Users;

namespace Basil.Application.Abstractions.Channels;

/// <summary>Ported from app/repositories/channels.py's Channel dataclass.</summary>
public sealed record Channel(
	int Id,
	string Name,
	string Topic,
	UserPrivileges ReadPrivilege,
	UserPrivileges WritePrivilege,
	bool AutoJoin);

/// <summary>Ported from app/repositories/channels.py's ChannelsRepository.</summary>
public interface IChannelRepository
{
	/// <summary>
	///     Every channel row, regardless of <see cref="Channel.AutoJoin" /> — used to seed the runtime
	///     registry at startup (ported from Channels.prepare(), which loads every DB channel
	///     unconditionally; <c>AutoJoin</c> only gates what's sent at login, not registry membership).
	/// </summary>
	Task<IReadOnlyList<Channel>> FetchAllAsync(CancellationToken cancellationToken = default);

	Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default);
}