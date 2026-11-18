using Basil.Application.Chat.Objects;
using Basil.Domain.Users;

namespace Basil.Host.Irc.Session;

internal sealed class IrcPresence
{
	private readonly Dictionary<int, HashSet<Channel>> _channels = [];
	private readonly Lock _gate = new();

	public bool Add(UserRecord user, Channel channel)
	{
		lock (_gate)
		{
			if (!_channels.TryGetValue(user.Id, out var set)) _channels[user.Id] = set = [];
			return set.Add(channel);
		}
	}

	public bool Remove(UserRecord user, Channel channel)
	{
		lock (_gate)
		{
			return _channels.TryGetValue(user.Id, out var set) && set.Remove(channel);
		}
	}

	public void Remove(Channel channel)
	{
		lock (_gate)
		{
			foreach (var set in _channels.Values) set.Remove(channel);
		}
	}

	public Channel[] Of(UserRecord user)
	{
		lock (_gate)
		{
			return _channels.TryGetValue(user.Id, out var set) ? [.. set] : [];
		}
	}

	public IReadOnlyCollection<Channel> Take(UserRecord user)
	{
		lock (_gate)
		{
			if (!_channels.Remove(user.Id, out var set)) return [];
			return set;
		}
	}
}