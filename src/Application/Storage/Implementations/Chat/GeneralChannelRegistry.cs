using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Chat;

namespace Basil.Application.Storage.Implementations.Chat;

/// <summary>The configured chat channels that are open, looked up by name.</summary>
/// <remarks>Room, spectator and private-message channels belong to their owners and are not listed here.</remarks>
internal sealed class GeneralChannelRegistry : IGeneralChannelRegistry
{
	private readonly ConcurrentDictionary<string, GeneralChannelSession> _channels =
		new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	public IReadOnlyCollection<GeneralChannelSession> All =>
		(IReadOnlyCollection<GeneralChannelSession>)_channels.Values;

	/// <inheritdoc />
	public GeneralChannelSession? Find(string name)
	{
		return _channels.GetValueOrDefault(name);
	}

	/// <inheritdoc />
	bool IGeneralChannelRegistry.Add(GeneralChannelSession channel)
	{
		return _channels.TryAdd(channel.Channel.Name, channel);
	}

	/// <inheritdoc />
	bool IGeneralChannelRegistry.Remove(GeneralChannelSession channel)
	{
		return _channels.TryRemove(new KeyValuePair<string, GeneralChannelSession>(channel.Channel.Name, channel));
	}
}