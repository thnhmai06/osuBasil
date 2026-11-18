using System.Collections.Concurrent;
using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Connections;

namespace Basil.Application.Chat.Registry;

public sealed class SpectatorChannels
{
	private readonly ConcurrentDictionary<StableConnection, SpectatorChannel> _channels = new();
	private readonly ChannelContext _context;

	internal SpectatorChannels(ChannelContext context)
	{
		_context = context;
	}

	public SpectatorChannel Of(StableConnection host)
	{
		return _channels.GetOrAdd(host, client => new SpectatorChannel(client, _context));
	}

	internal SpectatorChannel? Find(StableConnection host)
	{
		return _channels.GetValueOrDefault(host);
	}

	internal SpectatorChannel? Remove(StableConnection host)
	{
		return _channels.TryRemove(host, out var channel) ? channel : null;
	}
}