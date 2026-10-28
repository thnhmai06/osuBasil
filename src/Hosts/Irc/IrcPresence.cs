using Basil.Application.Chat.Runtime;
using Basil.Domain.Users;

namespace Basil.Host.Irc;

/// <summary>Remembers in which channels each user is visible to IRC clients, so a user shows up once however many connections they have.</summary>
internal sealed class IrcPresence
{
	private readonly Dictionary<int, HashSet<Channel>> _channels = [];
	private readonly Lock _gate = new();

	/// <summary>Records that a user is in a channel.</summary>
	/// <returns><see langword="true" /> when the user was not in it before.</returns>
	public bool Add(UserRecord user, Channel channel)
	{
		lock (_gate)
		{
			if (!_channels.TryGetValue(user.Id, out var set)) _channels[user.Id] = set = [];
			return set.Add(channel);
		}
	}

	/// <summary>Records that a user left a channel.</summary>
	/// <returns><see langword="true" /> when the user was in it.</returns>
	public bool Remove(UserRecord user, Channel channel)
	{
		lock (_gate)
		{
			return _channels.TryGetValue(user.Id, out var set) && set.Remove(channel);
		}
	}

	/// <summary>Forgets a closed channel.</summary>
	public void Remove(Channel channel)
	{
		lock (_gate)
		{
			foreach (var set in _channels.Values) set.Remove(channel);
		}
	}

	/// <summary>Lists the channels a user is in.</summary>
	/// <param name="user">The user.</param>
	/// <returns>A snapshot of the channels.</returns>
	public Channel[] Of(UserRecord user)
	{
		lock (_gate)
		{
			return _channels.TryGetValue(user.Id, out var set) ? [.. set] : [];
		}
	}

	/// <summary>Takes every channel a user was in.</summary>
	/// <returns>The channels, empty when the user was in none.</returns>
	public IReadOnlyCollection<Channel> Take(UserRecord user)
	{
		lock (_gate)
		{
			if (!_channels.Remove(user.Id, out var set)) return [];
			return set;
		}
	}
}
