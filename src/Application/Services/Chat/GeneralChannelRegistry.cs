using System.Collections.Concurrent;
using Basil.Application.Objects.Chat;

namespace Basil.Application.Services.Chat;

/// <summary>The configured chat channels that are open, looked up by name.</summary>
/// <remarks>Room, spectator and private-message channels belong to their owners and are not listed here.</remarks>
internal sealed class GeneralChannelRegistry : IGeneralChannelRegistry
{
	private readonly ConcurrentDictionary<string, GeneralChannel> _channels =
		new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	public IReadOnlyDictionary<string, GeneralChannel> All => _channels;

	/// <inheritdoc />
	bool IGeneralChannelRegistry.Add(GeneralChannel channel)
	{
		return _channels.TryAdd(channel.Name, channel);
	}

	/// <inheritdoc />
	bool IGeneralChannelRegistry.Remove(GeneralChannel channel)
	{
		return _channels.TryRemove(new KeyValuePair<string, GeneralChannel>(channel.Name, channel));
	}

	/// <summary>Finds an open general channel by name.</summary>
	public GeneralChannel? Find(string name)
	{
		return _channels.GetValueOrDefault(name);
	}
}