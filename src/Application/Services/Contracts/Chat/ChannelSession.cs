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

	/// <summary>Lists the members a posted message is shown to.</summary>
	/// <param name="posted">The message as the channel announced it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The members who do not block the author; every member when nobody blocks them.</returns>
	/// <remarks>
	///     A member who blocks the author is not shown the message, silently, and the author is not told. A block
	///     yields to a higher right: an author holding <see cref="Permissions.ChatMessageAnyone" />, and the server
	///     itself, reach every member. The author's own connections are kept; the transport decides whether to echo.
	/// </remarks>
	public async Task<IReadOnlyList<Connection>> RecipientsAsync(ChannelMessagePosted posted,
		CancellationToken cancellationToken = default)
	{
		var author = posted.Message.Author;
		var members = Members.ToArray();
		if (posted.By is not { } by ||
		    by.Session.Check(Permissions.ChatMessageAnyone, Context.Clock.GetUtcNow()) is Access.Allowed ||
		    this is PmChannelSession)
			return members;

		var blockers = new HashSet<UserRecord>();
		foreach (var user in members.Select(member => member.User).Where(user => !user.Equals(author)).Distinct())
		{
			var relationships = await Context.Relationships.ListAsync(user, cancellationToken);
			if (relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
			                                      relationship.Target.Equals(author)))
				blockers.Add(user);
		}

		return blockers.Count == 0 ? members : [.. members.Where(member => !blockers.Contains(member.User))];
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
