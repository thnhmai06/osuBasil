using System.Collections.Concurrent;
using Basil.Application.Chat.Objects;
using Basil.Application.Sessions.Objects;

namespace Basil.Application.Chat.Registry;

public sealed class PmChannels
{
	private readonly ConcurrentDictionary<UserSessions, PmChannel> _channels = new(ReferenceEqualityComparer.Instance);
	private readonly ChannelContext _context;

	internal PmChannels(ChannelContext context)
	{
		_context = context;
	}

	public PmChannel Of(UserSessions owner)
	{
		if (!owner.IsOnline)
		{
			if (_channels.TryRemove(owner, out var gone))
			{
				gone.Close();
				return gone;
			}

			return new PmChannel(owner, _context);
		}

		var channel = _channels.GetOrAdd(owner, static (current, context) => new PmChannel(current, context), _context);
		if (!owner.IsOnline)
		{
			_channels.TryRemove(new KeyValuePair<UserSessions, PmChannel>(owner, channel));
			channel.Close();
			return channel;
		}

		var now = _context.Clock.GetUtcNow();
		foreach (var connection in owner.Connections.Where(connection =>
			         connection.IsOpen && channel.CanRead(connection, connection.EffectivePrivileges(now))))
			channel.ScopeTo(connection).Join();

		return channel;
	}

	internal PmChannel? Find(UserSessions owner)
	{
		return _channels.GetValueOrDefault(owner);
	}

	internal PmChannel? Remove(UserSessions owner)
	{
		return _channels.TryRemove(owner, out var channel) ? channel : null;
	}
}