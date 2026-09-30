using System.Threading.Channels;
using Basil.Domain.Chat;
using Basil.Domain.Utilities;
using Basil.Application.Common.Events;
using Basil.Application.Sessions;

namespace Basil.Application.Chat;

/// <summary>A chat channel while it is open: its members and what happens in it.</summary>
public abstract class ChatChannelSession : IEventPublisher<ChatChannelEvent>
{
	/// <summary>The longest message kept; longer messages are cut.</summary>
	public const int MaxMessageLength = 2000;

	private readonly Channel<ChatChannelEvent> _events =
		System.Threading.Channels.Channel.CreateUnbounded<ChatChannelEvent>();

	private readonly ConcurrentSet<Connection> _members = [];
	private readonly Lock _sync = new();
	private volatile bool _closed;

	/// <summary>Opens a runtime channel for a chat channel.</summary>
	protected ChatChannelSession(ChatChannel channel, TimeProvider time)
	{
		Channel = channel;
		Time = time;
		Emit(new ChatChannelOpened(this));
	}

	/// <summary>Gets the chat channel this session runs.</summary>
	public ChatChannel Channel { get; }

	/// <summary>Gets the clock the channel reads the current time from.</summary>
	private protected TimeProvider Time { get; }

	/// <summary>Gets the channel name, without a leading <c>#</c>.</summary>
	public string Name => Channel.Name;

	/// <summary>Gets the connections currently in the channel.</summary>
	public IReadOnlySet<Connection> Members => _members;

	/// <inheritdoc />
	public ChannelReader<ChatChannelEvent> Events => _events.Reader;

	/// <summary>Gets a value that indicates whether a connection may read the channel.</summary>
	public abstract bool CanRead(Connection connection);

	/// <summary>Gets a value that indicates whether a connection may write to the channel.</summary>
	public abstract bool CanWrite(Connection connection);

	/// <summary>Gets a value that indicates whether only members may post.</summary>
	protected virtual bool PostRequiresMembership => true;

	/// <summary>Gets a value that indicates whether the channel currently accepts messages.</summary>
	protected virtual bool AcceptsMessages => true;

	/// <summary>Joins a connection to the channel.</summary>
	/// <param name="by">The connection joining.</param>
	/// <returns>The outcome of the join.</returns>
	public ChannelJoinResult Join(Connection by)
	{
		lock (_sync)
		{
			if (_closed) return ChannelJoinResult.Closed;
			if (!CanRead(by)) return ChannelJoinResult.NoPermission;
			if (_members.Contains(by)) return ChannelJoinResult.AlreadyMember;

			if (!by.Type.AllowsMany())
			{
				var old = _members.FirstOrDefault(m => m.User.Equals(by.User) && m.Type == by.Type);
				if (old is not null)
				{
					if (old.IsOpen) return ChannelJoinResult.AlreadyMember;
					_members.Remove(old);
					Emit(new MemberParted(this, old, false));
				}
			}

			_members.Add(by);
			Emit(new MemberJoined(this, by));
			return ChannelJoinResult.Joined;
		}
	}

	/// <summary>Parts a connection from the channel.</summary>
	/// <param name="by">The connection parting.</param>
	/// <returns>The outcome of the part.</returns>
	public ChannelPartResult Part(Connection by)
	{
		lock (_sync)
		{
			if (!_members.Remove(by)) return ChannelPartResult.NotMember;
			Emit(new MemberParted(this, by, false));
			return ChannelPartResult.Parted;
		}
	}

	/// <summary>Posts a message to the channel.</summary>
	/// <param name="by">The connection posting.</param>
	/// <param name="text">The message text.</param>
	/// <returns>The outcome of the post.</returns>
	public ChannelPostResult Post(Connection by, string text)
	{
		if (_closed) return ChannelPostResult.Closed;

		var now = Time.GetUtcNow();
		if (by.User.Value.SilenceEndsAt > now) return ChannelPostResult.Silenced;
		if (string.IsNullOrWhiteSpace(text)) return ChannelPostResult.Empty;
		if (PostRequiresMembership && !_members.Contains(by)) return ChannelPostResult.NotMember;
		if (!CanWrite(by)) return ChannelPostResult.NoWritePermission;
		if (!AcceptsMessages) return ChannelPostResult.TargetSilenced;

		var truncated = text.Length > MaxMessageLength;
		var message = new ChatMessage(by.User, truncated ? text[..MaxMessageLength] : text, now);
		Emit(new MessagePosted(this, message, truncated));
		OnPosted(by, now);
		return ChannelPostResult.Posted;
	}

	/// <summary>Called after a message has been posted.</summary>
	/// <param name="by">The connection that posted.</param>
	/// <param name="now">The timestamp of the post.</param>
	private protected virtual void OnPosted(Connection by, DateTimeOffset now)
	{
	}

	/// <summary>Removes a connection from the channel as a kick.</summary>
	/// <param name="connection">The connection to remove.</param>
	internal void Kick(Connection connection)
	{
		lock (_sync)
		{
			if (_members.Remove(connection))
				Emit(new MemberParted(this, connection, true));
		}
	}

	/// <summary>Closes the channel, removing every member and completing the event channel.</summary>
	internal void Close()
	{
		lock (_sync)
		{
			if (_closed) return;
			_closed = true;

			foreach (var member in _members.ToArray())
			{
				_members.Remove(member);
				Emit(new MemberParted(this, member, true));
			}

			Emit(new ChatChannelClosed(this));
			_events.Writer.TryComplete();
		}
	}

	/// <summary>Gets the lock that guards membership changes.</summary>
	private protected Lock Sync => _sync;

	/// <summary>Adds a member without emitting an event; the caller holds <see cref="Sync" /> and reports the change itself.</summary>
	/// <returns><see langword="true" /> if the connection was not already a member.</returns>
	private protected bool AddMember(Connection connection) => _members.Add(connection);

	/// <summary>Removes a member without emitting an event; the caller holds <see cref="Sync" /> and reports the change itself.</summary>
	/// <returns><see langword="true" /> if the connection was a member.</returns>
	private protected bool RemoveMember(Connection connection) => _members.Remove(connection);

	private protected void Emit(ChatChannelEvent @event) => _events.Writer.TryWrite(@event);
}