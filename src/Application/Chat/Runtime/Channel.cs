using Basil.Application.Users.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Runtime;

/// <summary>A chat channel while it is open: its members and what happens in it.</summary>
public abstract class Channel
{
	/// <summary>The longest message kept; longer messages are cut.</summary>
	public const int MaxMessageLength = 2000;

	private readonly ConcurrentSet<Connection> _members = [];
	private readonly Lock _sync = new();
	private volatile bool _closed;

	/// <summary>Initializes the channel and the services it needs to act.</summary>
	/// <param name="context">The channel's runtime dependencies.</param>
	internal Channel(ChannelContext context)
	{
		Context = context;
	}

	/// <summary>The runtime dependencies used by this channel.</summary>
	internal ChannelContext Context { get; }

	/// <summary>
	///     Gets the channel name: a channel several users take part in is named <c>#name</c>; a private-message channel
	///     is named after its owner, without <c>#</c>.
	/// </summary>
	public abstract string Name { get; }

	/// <summary>Gets the topic shown to users who join the channel.</summary>
	public abstract string Topic { get; }

	/// <summary>Gets the connections currently in the channel.</summary>
	public IReadOnlySet<Connection> Members => _members;

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

	/// <summary>Checks whether a connection may read the channel and so join it.</summary>
	/// <param name="connection">The connection to check.</param>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	internal abstract bool CanRead(Connection connection, DateTimeOffset now);

	/// <summary>Checks whether a connection may write to the channel.</summary>
	/// <param name="connection">The connection to check.</param>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	internal abstract bool CanWrite(Connection connection, DateTimeOffset now);

	/// <summary>Creates a channel scope for a connection.</summary>
	/// <param name="by">The connection acting on this channel.</param>
	/// <returns>A scope for the channel and connection.</returns>
	public ScopedChannel ScopeTo(Connection by)
	{
		return new ScopedChannel(this, by);
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
	///     yields to a higher right: an author holding <see cref="UserPermissions.ChatMessageAnyone" />, and the server
	///     itself, reach every member. The author's own connections are kept; the transport decides whether to echo.
	/// </remarks>
	public async Task<IReadOnlyList<Connection>> RecipientsAsync(ChannelMessagePosted posted,
		CancellationToken cancellationToken = default)
	{
		var author = posted.Message.Author;
		var members = Members.ToArray();
		if (posted.By is not { } by ||
		    by.User.EffectivePermissions(Context.Clock.GetUtcNow()).Allows(UserPermissions.ChatMessageAnyone) ||
		    this is PmChannel)
			return members;

		var blockers = new HashSet<UserRecord>();
		foreach (var user in members.Select(member => member.Record).Where(user => !user.Equals(author)).Distinct())
		{
			var relationships = await Context.Relationships.ListAsync(user, cancellationToken);
			if (relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
			                                      relationship.Target.Equals(author)))
				blockers.Add(user);
		}

		return blockers.Count == 0 ? members : [.. members.Where(member => !blockers.Contains(member.Record))];
	}

	/// <summary>Posts a message under the given author and reports the connection that posted it.</summary>
	/// <param name="author">The user shown as the author.</param>
	/// <param name="text">The message text.</param>
	/// <param name="notice">Whether the message is a notice.</param>
	/// <param name="now">The time the message was posted.</param>
	/// <param name="by">The connection that posted the message, or <see langword="null" /> for the server.</param>
	public void PostMessage(UserRecord author, string text, bool notice, DateTimeOffset now, Connection? by)
	{
		var truncated = text.Length > MaxMessageLength;
		var message = new Message
		{
			Author = author, Content = truncated ? text[..MaxMessageLength] : text, Timestamp = now, IsNotice = notice
		};

		Message? awayReply = null;
		if (by is not null
		    && this is PmChannel { Owner.AwayMessage: { } away } pmChannel
		    && !ReferenceEquals(by.User, pmChannel.Owner)
		    && !notice)
			awayReply = new Message { Author = pmChannel.Owner.Record, Content = away, Timestamp = now };

		Context.Events.Publish(new ChannelMessagePosted(this, message, truncated, awayReply, by));
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

		Context.Events.Publish(new ChannelClosed(this, members));

		if (this is GeneralChannel general)
			Context.Registry.Remove(general);
	}
}