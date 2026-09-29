using System.Threading.Channels;
using Basil.Application.Models.Events;
using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Models.Sessions;

/// <summary>
///     A channel's runtime membership: which sessions currently have it joined. The channel's own
///     metadata (topic, privileges, auto-join) lives on the <see cref="IChannel" /> this session is
///     keyed to by <see cref="Channel" />, not copied here.
/// </summary>
/// <remarks>Thread-safe: concurrent joins and parts of the same channel session do not corrupt its state.</remarks>
public sealed class ChannelSession : IEventSource<ChannelEvent>, IEquatable<ChannelSession>
{
	private readonly System.Threading.Channels.Channel<ChannelEvent> _events =
		System.Threading.Channels.Channel.CreateUnbounded<ChannelEvent>();
	private readonly ConcurrentSet<UserSession> _members = [];

	/// <summary>Gets the channel this session tracks membership for.</summary>
	public required IChannel Channel { get; init; }

	/// <summary>Gets the name of the channel this session tracks membership for.</summary>
	public string Name => Channel.Name;

	/// <summary>Gets the sessions currently joined to the channel.</summary>
	public IReadOnlySet<UserSession> Members => _members;

	/// <inheritdoc />
	public ChannelReader<ChannelEvent> Events => _events.Reader;

	/// <summary>Gets a value that indicates whether <paramref name="user" /> may read the channel.</summary>
	/// <param name="user">The user to check.</param>
	/// <returns><see langword="true" /> if the user may read the channel; otherwise, <see langword="false" />.</returns>
	public bool CanRead(User user)
	{
		return user.Privilege.Has(Channel.ReadPrivilege);
	}

	/// <summary>Gets a value that indicates whether <paramref name="user" /> may write to the channel.</summary>
	/// <param name="user">The user to check.</param>
	/// <returns><see langword="true" /> if the user may write to the channel; otherwise, <see langword="false" />.</returns>
	public bool CanWrite(User user)
	{
		return user.Privilege.Has(Channel.WritePrivilege);
	}

	/// <summary>Adds <paramref name="member" /> to the channel and records that they joined.</summary>
	/// <param name="member">The session that joined.</param>
	internal void Add(UserSession member)
	{
		if (_members.Add(member))
			_events.Writer.TryWrite(new ChannelJoined(this, member));
	}

	/// <summary>Removes <paramref name="member" /> from the channel and records that they parted.</summary>
	/// <param name="member">The session that parted.</param>
	internal void Remove(UserSession member)
	{
		if (_members.Remove(member))
			_events.Writer.TryWrite(new ChannelParted(this, member));
	}

	/// <summary>Determines whether another channel session tracks the same channel.</summary>
	/// <param name="other">The channel session to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="other" /> refers to the same
	///     <see cref="IChannel" />; otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(ChannelSession? other)
	{
		if (other is null) return false;
		return Channel.Equals(other.Channel);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is ChannelSession other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Channel.GetHashCode();
	}
}
