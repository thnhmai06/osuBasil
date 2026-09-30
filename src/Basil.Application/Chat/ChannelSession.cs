using System.Threading.Channels;
using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Domain.Utilities;
using Basil.Application.Common.Events;
using Basil.Application.Sessions;

namespace Basil.Application.Chat;

/// <summary>
///     A channel's runtime membership: which connections currently have it joined. The channel's own
///     metadata (topic, privileges, auto-join) lives on the <see cref="IChannel" /> this session is
///     keyed to by <see cref="Channel" />, not copied here.
/// </summary>
/// <remarks>Thread-safe: concurrent joins and parts of the same channel session do not corrupt its state.</remarks>
public sealed class ChannelSession : IEventPublisher<ChannelEvent>, IEquatable<ChannelSession>
{
	private readonly Channel<ChannelEvent> _events =
		System.Threading.Channels.Channel.CreateUnbounded<ChannelEvent>();

	private readonly ConcurrentSet<Connection> _members = [];

	/// <summary>Gets the channel this session tracks membership for.</summary>
	public required IChannel Channel { get; init; }

	/// <summary>Gets the name of the channel this session tracks membership for.</summary>
	public string Name => Channel.Name;

	/// <summary>Gets the connections currently joined to the channel.</summary>
	public IReadOnlySet<Connection> Members => _members;

	/// <inheritdoc />
	public ChannelReader<ChannelEvent> Events => _events.Reader;

	/// <summary>Gets a value that indicates whether <paramref name="connection" /> may read the channel.</summary>
	/// <param name="connection">The connection to check.</param>
	/// <returns><see langword="true" /> if the connection may read the channel; otherwise, <see langword="false" />.</returns>
	public bool CanRead(Connection connection)
	{
		return connection.User.Value.Privilege.Has(Channel.ReadPrivilege);
	}

	/// <summary>Gets a value that indicates whether <paramref name="connection" /> may write to the channel.</summary>
	/// <param name="connection">The connection to check.</param>
	/// <returns><see langword="true" /> if the connection may write to the channel; otherwise, <see langword="false" />.</returns>
	public bool CanWrite(Connection connection)
	{
		return connection.User.Value.Privilege.Has(Channel.WritePrivilege);
	}

	/// <summary>Joins a connection to the channel.</summary>
	/// <param name="by">The connection joining.</param>
	/// <returns><see langword="true" /> if the connection joined; <see langword="false" /> if it may not read the channel or was already a member.</returns>
	public bool Join(Connection by)
	{
		return CanRead(by) && _members.Add(by) && _events.Writer.TryWrite(new MemberJoined(this, by));
	}

	/// <summary>Parts a connection from the channel.</summary>
	/// <param name="by">The connection parting.</param>
	/// <remarks>Parting a connection that is not a member does nothing.</remarks>
	public void Part(Connection by)
	{
		if (_members.Remove(by))
			_events.Writer.TryWrite(new MemberParted(this, by));
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