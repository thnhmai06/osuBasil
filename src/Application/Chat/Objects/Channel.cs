using Basil.Application.Chat.Capabilities;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

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
	/// <param name="privileges">The effective privileges the connection holds.</param>
	internal abstract bool CanRead(Connection connection, UserPrivileges privileges);

	/// <summary>Checks whether a connection may write to the channel.</summary>
	/// <param name="connection">The connection to check.</param>
	/// <param name="privileges">The effective privileges the connection holds.</param>
	internal abstract bool CanWrite(Connection connection, UserPrivileges privileges);

	/// <summary>Creates a channel scope for a connection.</summary>
	/// <param name="actor">The connection acting on this channel.</param>
	/// <returns>A scope for the channel and connection.</returns>
	public ScopedChannel ScopeTo(Connection actor)
	{
		return new ScopedChannel(this, actor);
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
	///     yields to a higher right: an author holding <see cref="UserPrivileges.Moderator" />, and the server
	///     itself, reach every member. The author's own connections are kept; the transport decides whether to echo.
	/// </remarks>
	public async Task<IReadOnlyList<Connection>> RecipientsAsync(ChannelMessagePosted posted,
		CancellationToken cancellationToken = default)
	{
		var author = posted.Message.Author;
		var members = Members.ToArray();
		if (posted.Actor is null || posted.BypassesBlocks || this is PmChannel) return members;

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
	/// <param name="actor">The connection that posted the message, or <see langword="null" /> for the server.</param>
	/// <param name="bypassesBlocks">Whether the message reaches members who block the author.</param>
	public void PostMessage(UserRecord author, string text, bool notice, DateTimeOffset now, Connection? actor,
		bool bypassesBlocks = false)
	{
		var truncated = text.Length > MaxMessageLength;
		var message = new Message
		{
			Author = author, Content = truncated ? text[..MaxMessageLength] : text, Timestamp = now, IsNotice = notice
		};

		Message? awayReply = null;
		if (actor is not null
		    && this is PmChannel { Owner.AwayMessage: { } away } pmChannel
		    && !ReferenceEquals(actor.User, pmChannel.Owner)
		    && !notice)
			awayReply = new Message { Author = pmChannel.Owner.Record, Content = away, Timestamp = now };

		Context.Events.Publish(new ChannelMessagePosted(this, message, truncated, awayReply, actor, bypassesBlocks));
	}

	/// <summary>Adds a connection to the channel's members.</summary>
	/// <param name="connection">The connection that joins.</param>
	/// <param name="privileges">The effective privileges the connection holds.</param>
	/// <returns>Ok, Closed, AccessDenied or AlreadyMember.</returns>
	internal ChannelOutcome Join(Connection connection, UserPrivileges privileges)
	{
		using var scope = Enter();
		if (IsClosed) return ChannelOutcome.Closed;
		if (!CanRead(connection, privileges)) return ChannelOutcome.AccessDenied;
		if (Members.Contains(connection)) return ChannelOutcome.AlreadyMember;

		Connection? replaced = null;
		if (connection.Type.ReplacesOnJoin())
		{
			replaced = Members.FirstOrDefault(member =>
				member.Record.Equals(connection.Record) && member.Type == connection.Type);
			if (replaced is not null)
			{
				if (replaced.IsOpen) return ChannelOutcome.AlreadyMember;
				MutableMembers.Remove(replaced);
			}
		}

		MutableMembers.Add(connection);
		Context.Events.Publish(new ChannelMemberJoined(this, connection, replaced));
		return ChannelOutcome.Ok;
	}

	/// <summary>Removes a connection from the channel's members.</summary>
	/// <param name="connection">The connection that leaves.</param>
	/// <returns>Ok, or NotMember when it was not a member.</returns>
	internal ChannelOutcome Part(Connection connection)
	{
		using var scope = Enter();
		if (!MutableMembers.Remove(connection)) return ChannelOutcome.NotMember;
		Context.Events.Publish(new ChannelMemberParted(this, connection, false));
		return ChannelOutcome.Ok;
	}

	/// <summary>Posts a message from a connection to the channel.</summary>
	/// <param name="actor">The connection that posts.</param>
	/// <param name="privileges">The effective privileges the poster holds.</param>
	/// <param name="recipientSilenced">
	///     Whether the user who receives a private message is silenced; <see langword="null" /> for any other channel.
	/// </param>
	/// <param name="text">The message text.</param>
	/// <param name="notice">Whether the message is a notice.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, Closed, Empty, InvalidText, NotMember, Silenced, AccessDenied, Blocked or TargetSilenced.</returns>
	internal async Task<ChannelOutcome> PostAsync(Connection actor, UserPrivileges privileges,
		bool? recipientSilenced, string text, bool notice, CancellationToken cancellationToken)
	{
		if (IsClosed) return ChannelOutcome.Closed;
		if (string.IsNullOrEmpty(text)) return ChannelOutcome.Empty;
		if (!Message.IsValidContent(text)) return ChannelOutcome.InvalidText;
		if (this is not PmChannel && !Members.Contains(actor)) return ChannelOutcome.NotMember;

		var now = Context.Clock.GetUtcNow();
		if (actor.User.Record.Value.IsSilenced(now)) return ChannelOutcome.Silenced;
		if (!CanWrite(actor, privileges)) return ChannelOutcome.AccessDenied;

		var mayMessageAnyone = privileges.Allows(UserPrivileges.Moderator);
		if (this is PmChannel pm && recipientSilenced is { } recipient &&
		    await PrivateMessageRefusalAsync(pm, actor, mayMessageAnyone, recipient, cancellationToken) is { } refusal)
			return refusal;

		if (IsClosed) return ChannelOutcome.Closed;

		PostMessage(actor.Record, text, notice, now, actor, mayMessageAnyone);
		return ChannelOutcome.Ok;
	}

	private async Task<ChannelOutcome?> PrivateMessageRefusalAsync(PmChannel pm, Connection actor,
		bool mayMessageAnyone, bool recipientSilenced, CancellationToken cancellationToken)
	{
		if (!mayMessageAnyone)
		{
			var relationships = await Context.Relationships.ListAsync(pm.Owner.Record, cancellationToken);
			var self = actor.Record;
			var blocked = relationships.Any(relationship =>
				relationship.Type == RelationshipType.Block && relationship.Target.Equals(self));
			var friend = relationships.Any(relationship =>
				relationship.Type == RelationshipType.Friend && relationship.Target.Equals(self));
			if (blocked || (pm.Owner.PmPrivate && !friend)) return ChannelOutcome.Blocked;
		}

		if (!pm.Owner.Record.IsFriendOfEveryone() && recipientSilenced)
			return ChannelOutcome.TargetSilenced;

		return null;
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