using System.Threading.Channels;
using Basil.Application.Models.Events;
using Basil.Application.Models.Notifications;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Models.Sessions;

/// <summary>
///     The server-side runtime identity shared by every online connection for an account, created at
///     login and discarded at logout. Concrete sessions are either an <see cref="IrcSession" /> (chat
///     and commands only) or a <see cref="GameSession" /> (a real osu! client) — the same account may
///     hold one of each at once.
/// </summary>
/// <remarks>
///     Holds only runtime state Domain does not have; the account's own data (name, privileges,
///     country) always comes from <see cref="User" />. Thread-safe: concurrent channel membership
///     changes on the same session do not corrupt its state.
/// </remarks>
public abstract class UserSession : IEventSource<SessionEvent>, IEquatable<UserSession>
{
	private readonly ConcurrentSet<ChannelSession> _channels = [];
	private readonly Channel<SessionEvent> _events = Channel.CreateUnbounded<SessionEvent>();

	/// <summary>Gets the user this session represents.</summary>
	public abstract User User { get; }

	/// <summary>Gets the time this session was created.</summary>
	public abstract DateTimeOffset LoginTime { get; }

	/// <summary>Gets the live transport connection this session sends notifications through.</summary>
	public required IClientConnection Connection { get; init; }

	/// <summary>Gets or sets the time of the last activity received from the client.</summary>
	public DateTimeOffset LastActiveAt { get; set; }

	/// <summary>Gets or sets the away message shown to other users, or <see langword="null" /> when not away.</summary>
	public string? AwayMessage { get; set; }

	/// <summary>Gets the channels this session has joined.</summary>
	public IReadOnlySet<ChannelSession> Channels => _channels;

	/// <inheritdoc />
	public ChannelReader<SessionEvent> Events => _events.Reader;

	/// <summary>Sends a message to this session's client.</summary>
	/// <param name="notification">The message to send.</param>
	public void Notify(Notification notification)
	{
		Connection.Send(notification);
	}

	/// <summary>
	///     Joins this session to <paramref name="channel" />, updating both sides of the membership.
	/// </summary>
	/// <param name="channel">The channel to join.</param>
	/// <exception cref="InvalidOperationException">
	///     <see cref="User" /> lacks read permission on the channel.
	/// </exception>
	public void Join(ChannelSession channel)
	{
		if (!channel.CanRead(User))
			throw new InvalidOperationException("The user does not have permission to read this channel.");

		if (_channels.Add(channel))
			channel.Add(this);
	}

	/// <summary>
	///     Parts this session from <paramref name="channel" />, updating both sides of the membership.
	/// </summary>
	/// <param name="channel">The channel to part.</param>
	public void Part(ChannelSession channel)
	{
		if (_channels.Remove(channel))
			channel.Remove(this);
	}

	/// <summary>Determines whether another session represents the same user through the same concrete type.</summary>
	/// <param name="other">The session to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="other" /> has the same concrete type and the
	///     same <see cref="User" />; otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(UserSession? other)
	{
		if (other is null) return false;
		if (GetType() != other.GetType()) return false;
		return User.Equals(other.User);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is UserSession other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(GetType(), User);
	}

	/// <summary>Records a runtime event that has occurred to this session.</summary>
	/// <param name="event">The event to record.</param>
	private protected void Record(SessionEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}
}
