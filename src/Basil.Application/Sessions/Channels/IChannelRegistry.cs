using Basil.Application.Abstractions.Channels;

namespace Basil.Application.Sessions.Channels;

/// <summary>
///     Represents the runtime registry of channels: DB-backed channels seeded from the channel
///     repository at startup, plus runtime-created instance channels such as spectator channels.
///     Each entry carries its own live membership state (see <see cref="ChannelSession" />).
/// </summary>
public interface IChannelRegistry
{
	/// <summary>Gets the channels players join automatically at login.</summary>
	IReadOnlyList<ChannelSession> AutoJoinChannels { get; }

	/// <summary>Gets a snapshot of every registered channel.</summary>
	IReadOnlyList<ChannelSession> All { get; }

	/// <summary>
	///     Replaces the registry contents with the DB-backed channels in <paramref name="channels" />,
	///     called once at startup.
	/// </summary>
	/// <param name="channels">The channels to register.</param>
	void Seed(IReadOnlyList<Channel> channels);

	/// <summary>
	///     Adds a runtime-created instance channel to the registry, such as a spectator channel.
	///     Instance channels are created and removed at runtime and are not backed by a database row.
	/// </summary>
	/// <param name="channel">The instance channel to register.</param>
	void Add(ChannelSession channel);

	/// <summary>
	///     Removes an instance channel from the registry, called once its last member leaves.
	/// </summary>
	/// <param name="name">The registry key of the channel to remove.</param>
	void Remove(string name);

	/// <summary>
	///     Gets the channel with the registry key <paramref name="name" />, or null if no channel is
	///     registered under that name.
	/// </summary>
	/// <param name="name">The registry key of the channel.</param>
	/// <returns>The matching channel, or null if none is registered.</returns>
	ChannelSession? GetByName(string name);
}