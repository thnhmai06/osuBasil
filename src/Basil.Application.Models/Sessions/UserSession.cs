using System.Collections.Concurrent;
using Basil.Application.Models.Notifications;
using Basil.Domain.Events;

namespace Basil.Application.Models.Sessions;

/// <summary>
///     The server-side runtime identity shared by every online connection for an account, created at
///     login and discarded at logout. Concrete sessions are either an <see cref="IrcSession" /> (chat
///     and commands only) or a <see cref="GameSession" /> (a real osu! client) — the same account may
///     hold one of each at once.
/// </summary>
/// <remarks>
///     Holds only runtime state Domain does not have; the account's own data (name, privileges,
///     country) always comes from the user repository via <see cref="UserId" />. Thread-safe:
///     concurrent channel membership changes and event recording on the same session do not corrupt
///     its state.
/// </remarks>
public abstract class UserSession : IHasDomainEvents
{
	private readonly ConcurrentDictionary<string, byte> _channels = new();
	private readonly DomainEventLog _events = new();
	private readonly Lock _eventsSync = new();

	/// <summary>Gets the id of the account this session belongs to.</summary>
	public required int UserId { get; init; }

	/// <summary>Gets the live transport connection this session sends notifications through.</summary>
	public required IClientConnection Connection { get; init; }

	/// <summary>Gets the time this session was created.</summary>
	public required DateTimeOffset LoginTime { get; init; }

	/// <summary>Gets or sets the time of the last activity received from the client.</summary>
	public DateTimeOffset LastActiveAt { get; set; }

	/// <summary>Gets or sets the away message shown to other users, or <see langword="null" /> when not away.</summary>
	public string? AwayMessage { get; set; }

	/// <summary>Gets the names of the channels this session has joined.</summary>
	public IReadOnlyCollection<string> Channels => _channels.Keys.ToArray();

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

	/// <summary>Sends a notification to this session's client.</summary>
	/// <param name="notification">The notification to send.</param>
	public void Notify(Notification notification)
	{
		Connection.Send(notification);
	}

	/// <summary>Records that this session joined a channel, for its own channel-membership bookkeeping.</summary>
	/// <param name="name">The name of the channel joined.</param>
	internal void AddChannel(string name)
	{
		_channels[name] = 0;
	}

	/// <summary>Records that this session parted a channel, for its own channel-membership bookkeeping.</summary>
	/// <param name="name">The name of the channel parted.</param>
	internal void RemoveChannel(string name)
	{
		_channels.TryRemove(name, out _);
	}

	/// <summary>Records a runtime event that has occurred to this session.</summary>
	/// <param name="domainEvent">The event to record.</param>
	private protected void Record(IDomainEvent domainEvent)
	{
		lock (_eventsSync)
		{
			_events.Record(domainEvent);
		}
	}
}