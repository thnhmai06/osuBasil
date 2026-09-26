using System.Collections.Concurrent;
using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Domain.Events;
using Basil.Domain.Users;

namespace Basil.Application.Models.Sessions;

/// <summary>
///     A channel's runtime membership: which sessions currently have it joined. The channel's own
///     metadata (topic, privileges, auto-join) lives on the <see cref="IChannel" /> this session is
///     keyed to by <see cref="Name" />, not copied here.
/// </summary>
/// <remarks>Thread-safe: concurrent joins and parts of the same channel session do not corrupt its state.</remarks>
public sealed class ChannelSession : IHasDomainEvents
{
	private readonly DomainEventLog _events = new();
	private readonly Lock _eventsSync = new();
	private readonly ConcurrentDictionary<int, byte> _memberIds = new();

	/// <summary>Gets the name of the channel this session tracks membership for.</summary>
	public required string Name { get; init; }

	/// <summary>Gets the ids of the sessions currently joined to the channel.</summary>
	public IReadOnlyCollection<int> MemberIds => _memberIds.Keys.ToArray();

	/// <inheritdoc />
	public IReadOnlyList<IDomainEvent> DomainEvents
	{
		get
		{
			lock (_eventsSync)
			{
				return [.. _events.Events];
			}
		}
	}

	/// <inheritdoc />
	public void ClearDomainEvents()
	{
		lock (_eventsSync)
		{
			_events.Clear();
		}
	}

	/// <summary>Gets a value that indicates whether <paramref name="user" /> may write to the channel.</summary>
	/// <param name="channel">The channel's metadata.</param>
	/// <param name="user">The user to check.</param>
	/// <returns><see langword="true" /> if the user may write to the channel; otherwise, <see langword="false" />.</returns>
	public static bool CanWrite(IChannel channel, User user)
	{
		return user.Privilege.Has(channel.WritePrivilege);
	}

	/// <summary>Joins a session to the channel, if it has read permission.</summary>
	/// <param name="channel">The channel's metadata.</param>
	/// <param name="user">The account of the session joining.</param>
	/// <param name="session">The session joining.</param>
	/// <exception cref="InvalidOperationException"><paramref name="user" /> lacks read permission on the channel.</exception>
	public void Join(IChannel channel, User user, UserSession session)
	{
		if (!user.Privilege.Has(channel.ReadPrivilege))
			throw new InvalidOperationException("The user does not have permission to read this channel.");

		_memberIds[session.UserId] = 0;
		session.AddChannel(Name);
		lock (_eventsSync)
		{
			_events.Record(new ChannelJoined(this, session));
		}
	}

	/// <summary>Parts a session from the channel.</summary>
	/// <param name="session">The session parting.</param>
	public void Part(UserSession session)
	{
		_memberIds.TryRemove(session.UserId, out _);
		session.RemoveChannel(Name);
		lock (_eventsSync)
		{
			_events.Record(new ChannelParted(this, session));
		}
	}
}