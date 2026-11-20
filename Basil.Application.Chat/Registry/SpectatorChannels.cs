using System.Collections.Concurrent;
using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Sessions;

namespace Basil.Application.Chat.Registry;

public sealed class SpectatorChannels
{
	private readonly ConcurrentDictionary<StableSession, SpectatorChannel> _channels = new();
	private readonly ChannelContext _context;

	internal SpectatorChannels(ChannelContext context)
	{
		_context = context;
	}

	public SpectatorChannel Of(StableSession host)
	{
		return _channels.GetOrAdd(host, static (client, context) => new SpectatorChannel(client, context), _context);
	}

	internal SpectatorChannel? Find(StableSession host)
	{
		return _channels.GetValueOrDefault(host);
	}

	internal SpectatorChannel? Remove(StableSession host)
	{
		return _channels.TryRemove(host, out var channel) ? channel : null;
	}
}