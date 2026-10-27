using System.Collections.Concurrent;
using Basil.Application.Runtime.Users;

namespace Basil.Application.Runtime.Chat;

/// <summary>The private-message channels of the users who are online.</summary>
/// <remarks>A user's channel is opened the first time it is needed and is removed when the user goes offline.</remarks>
public sealed class PmChannels
{
	private readonly ConcurrentDictionary<User, PmChannel> _channels = new(ReferenceEqualityComparer.Instance);
	private readonly ChannelContext _context;

	/// <summary>Creates the lookup of private-message channels.</summary>
	/// <param name="context">The runtime dependencies of the channels it opens.</param>
	internal PmChannels(ChannelContext context)
	{
		_context = context;
	}

	/// <summary>Gets the private-message channel of an online user, opening it the first time and adding their open connections.</summary>
	/// <param name="owner">The user who receives the messages.</param>
	/// <returns>The user's channel; for a user who is not online, a channel nobody can write to that is not remembered.</returns>
	public PmChannel Of(User owner)
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

		var channel = _channels.GetOrAdd(owner, current => new PmChannel(current, _context));
		if (!owner.IsOnline)
		{
			_channels.TryRemove(new KeyValuePair<User, PmChannel>(owner, channel));
			channel.Close();
			return channel;
		}

		var now = _context.Clock.GetUtcNow();
		foreach (var connection in owner.Connections.Where(connection =>
			         connection.IsOpen && channel.CanRead(connection, now)))
			channel.ScopeTo(connection).Join();

		return channel;
	}

	/// <summary>Gets a user's private-message channel if it has one.</summary>
	/// <param name="owner">The user.</param>
	/// <returns>The user's channel, or <see langword="null" /> when it has none.</returns>
	internal PmChannel? Find(User owner)
	{
		return _channels.GetValueOrDefault(owner);
	}

	/// <summary>Removes a user's private-message channel.</summary>
	/// <param name="owner">The user whose channel is removed.</param>
	/// <returns>The channel that was removed, or <see langword="null" /> when it had none.</returns>
	internal PmChannel? Remove(User owner)
	{
		return _channels.TryRemove(owner, out var channel) ? channel : null;
	}
}