using System.Collections.Concurrent;
using Basil.Application.Users.Connections;

namespace Basil.Application.Chat.Runtime;

/// <summary>The configured chat channels that are open, looked up by name.</summary>
/// <remarks>Room, spectator and private-message channels belong to their owners and are not listed here.</remarks>
public sealed class GeneralChannelRegistry
{
	private readonly ConcurrentDictionary<string, GeneralChannel> _channels =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Gets every open configured channel.</summary>
	public IReadOnlyDictionary<string, GeneralChannel> All => _channels;

	/// <summary>Adds a configured channel.</summary>
	/// <param name="channel">The channel to add.</param>
	/// <returns><see langword="true" /> if the channel was added; otherwise, <see langword="false" />.</returns>
	internal bool Add(GeneralChannel channel)
	{
		return _channels.TryAdd(channel.Name, channel);
	}

	/// <summary>Removes a configured channel.</summary>
	/// <param name="channel">The channel to remove.</param>
	/// <returns><see langword="true" /> if the channel was removed.</returns>
	internal bool Remove(GeneralChannel channel)
	{
		return _channels.TryRemove(new KeyValuePair<string, GeneralChannel>(channel.Name, channel));
	}

	/// <summary>Finds an open general channel by name.</summary>
	/// <param name="name">The channel name.</param>
	/// <returns>The open channel, or <see langword="null" /> when none has that name.</returns>
	public GeneralChannel? Find(string name)
	{
		return _channels.GetValueOrDefault(name);
	}

	/// <summary>Joins a connection to auto-join channels when the server itself calls it; it needs no rights.</summary>
	/// <param name="by">The connection that opened.</param>
	public void JoinAutoChannels(Connection by)
	{
		foreach (var channel in All.Values.Where(channel => channel.Record.Value.AutoJoin))
			channel.ScopeTo(by).Join();
	}

	/// <summary>Parts a connection from every open general channel when the server itself calls it; it needs no rights.</summary>
	/// <param name="by">The connection that closed.</param>
	public void PartAll(Connection by)
	{
		foreach (var channel in All.Values)
			channel.ScopeTo(by).Part();
	}
}