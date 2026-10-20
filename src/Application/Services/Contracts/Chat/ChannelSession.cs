using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>A chat channel while it is open: its members and what happens in it.</summary>
/// <remarks>Opens a runtime channel for a chat channel.</remarks>
public abstract class ChannelSession
{
	/// <summary>The longest message kept; longer messages are cut.</summary>
	public const int MaxMessageLength = 2000;

	private readonly ConcurrentSet<Connection> _members = [];
	private readonly Lock _sync = new();
	private volatile bool _closed;

	/// <summary>Initializes the channel and the services it needs to act.</summary>
	/// <param name="context">The channel's runtime dependencies.</param>
	internal ChannelSession(ChannelContext context)
	{
		Context = context;
	}

	/// <summary>The runtime dependencies used by this channel.</summary>
	internal ChannelContext Context { get; }

	/// <summary>Gets the chat channel this session runs.</summary>
	public abstract Channel Channel { get; }

	/// <summary>Gets the connections currently in the channel.</summary>
	public IReadOnlySet<Connection> Members => _members;

	/// <summary>Creates a channel scope for a connection.</summary>
	/// <param name="by">The connection acting on this channel.</param>
	/// <returns>A scope for the channel and connection.</returns>
	public ScopedChannel ScopeTo(Connection by)
	{
		return new ScopedChannel(this, by);
	}

	/// <summary>
	///     Gets the connections currently in the channel, for changing them; the caller holds the scope and reports the
	///     change itself.
	/// </summary>
	internal ISet<Connection> MutableMembers => _members;

	/// <summary>Gets a value that indicates whether the channel has been closed.</summary>
	public bool IsClosed
	{
		get => _closed;
		internal set => _closed = value;
	}

	/// <summary>
	///     Enters the scope in which the channel's members change one at a time.
	/// </summary>
	/// <returns>A scope that must be disposed when the membership change is complete.</returns>
	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	/// <summary>Posts a message under the given author and reports the connection that posted it.</summary>
	/// <param name="author">The user shown as the author.</param>
	/// <param name="text">The message text.</param>
	/// <param name="notice">Whether the message is a notice.</param>
	/// <param name="now">The time the message was posted.</param>
	/// <param name="by">The connection that posted the message, or <see langword="null" /> for the server.</param>
	internal void PostMessage(UserRecord author, string text, bool notice, DateTimeOffset now, Connection? by)
	{
		var truncated = text.Length > MaxMessageLength;
		var message = new Message(author, truncated ? text[..MaxMessageLength] : text, now, notice);

		Message? awayReply = null;
		if (by is not null
		    && this is PmChannelSession { Owner.AwayMessage: { } away } pmChannel
		    && !ReferenceEquals(by.Session, pmChannel.Owner)
		    && !notice)
			awayReply = new Message(pmChannel.Owner.User, away, now);

		Context.Events.Emit(new ChannelMessagePosted(this, message, truncated, awayReply, by));
	}

	/// <summary>Closes this channel and removes every member.</summary>
	internal void Close()
	{
		using var scope = Enter();
		if (IsClosed) return;
		IsClosed = true;

		var members = Members.ToArray();
		foreach (var member in members)
			MutableMembers.Remove(member);

		Context.Events.Emit(new ChannelClosed(this, members));

		if (this is GeneralChannelSession general)
			Context.Registry.Remove(general);
	}
}
