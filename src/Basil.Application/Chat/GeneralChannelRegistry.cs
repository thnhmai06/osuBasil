using System.Collections.Concurrent;
using Basil.Application.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Chat;

/// <summary>The configured chat channels that are open, looked up by name.</summary>
/// <remarks>Room, spectator and private-message channels belong to their owners and are not listed here.</remarks>
public sealed class GeneralChannelRegistry(TimeProvider time)
{
	private readonly ConcurrentDictionary<string, GeneralChannelSession> _channels =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Gets every open configured channel.</summary>
	public IEnumerable<GeneralChannelSession> All => _channels.Values;

	/// <summary>Finds an open configured channel by name, ignoring case.</summary>
	/// <param name="name">The channel name, including its leading <c>#</c>.</param>
	/// <returns>The channel, or <see langword="null" /> when no configured channel has that name.</returns>
	public GeneralChannelSession? Find(string name)
	{
		return _channels.GetValueOrDefault(name);
	}

	/// <summary>Opens a configured channel.</summary>
	/// <param name="channel">The channel to open.</param>
	/// <returns>The open channel, or <see langword="null" /> when a channel with that name is already open.</returns>
	public GeneralChannelSession? Open(GeneralChannel channel)
	{
		var session = new GeneralChannelSession(channel, time);
		if (_channels.TryAdd(channel.Name, session)) return session;
		session.Close();
		return null;
	}

	/// <summary>Closes an open configured channel, removing every member.</summary>
	/// <param name="channel">The channel to close.</param>
	public void Close(GeneralChannelSession channel)
	{
		if (_channels.TryRemove(new KeyValuePair<string, GeneralChannelSession>(channel.Name, channel)))
			channel.Close();
	}

	/// <summary>Joins a connection to every auto-join channel it may read.</summary>
	/// <param name="connection">The connection that just opened.</param>
	public void JoinAutoChannels(Connection connection)
	{
		foreach (var channel in _channels.Values)
			if (channel.Channel.AutoJoin)
				channel.Join(connection);
	}

	/// <summary>Removes a connection from every configured channel; calling it again does nothing.</summary>
	/// <param name="connection">The connection that closed.</param>
	public void PartAll(Connection connection)
	{
		foreach (var channel in _channels.Values)
			channel.Part(connection);
	}
}