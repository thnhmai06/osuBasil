using Basil.Application.Chat.Capabilities;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Shared;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Objects;

public abstract class Channel
{
	public const int MaxMessageLength = 2000;

	private readonly ConcurrentSet<Connection> _members = [];
	private readonly Lock _sync = new();
	private volatile bool _closed;

	internal Channel(ChannelContext context)
	{
		Context = context;
	}

	internal ChannelContext Context { get; }

	public abstract string Name { get; }

	public abstract string Topic { get; }

	public IReadOnlySet<Connection> Members => _members;

	internal ISet<Connection> MutableMembers => _members;

	public bool IsClosed
	{
		get => _closed;
		internal set => _closed = value;
	}

	internal abstract bool CanRead(Connection connection, UserPrivileges privileges);

	internal abstract bool CanWrite(Connection connection, UserPrivileges privileges);

	public ScopedChannel ScopeTo(Connection actor)
	{
		return new ScopedChannel(this, actor);
	}

	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	public async Task<IReadOnlyList<Connection>> RecipientsAsync(ChannelMessagePosted posted,
		CancellationToken ct = default)
	{
		var author = posted.Message.Author;
		var members = Members.ToArray();
		if (posted.Actor is null || posted.BypassesBlocks || this is PmChannel) return members;

		var blockers = new HashSet<UserRecord>();
		foreach (var user in members.Select(member => member.Record).Where(user => !user.Equals(author)).Distinct())
		{
			var relationships = await Context.Relationships.ListAsync(user, ct);
			if (relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
			                                      relationship.Target.Equals(author)))
				blockers.Add(user);
		}

		return blockers.Count == 0 ? members : [.. members.Where(member => !blockers.Contains(member.Record))];
	}

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

	internal ChannelOutcome Part(Connection connection)
	{
		using var scope = Enter();
		if (!MutableMembers.Remove(connection)) return ChannelOutcome.NotMember;
		Context.Events.Publish(new ChannelMemberParted(this, connection, false));
		return ChannelOutcome.Ok;
	}

	internal async Task<ChannelOutcome> PostAsync(Connection actor, UserPrivileges privileges,
		bool? recipientSilenced, string text, bool notice, CancellationToken ct)
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
		    await PrivateMessageRefusalAsync(pm, actor, mayMessageAnyone, recipient, ct) is { } refusal)
			return refusal;

		if (IsClosed) return ChannelOutcome.Closed;

		PostMessage(actor.Record, text, notice, now, actor, mayMessageAnyone);
		return ChannelOutcome.Ok;
	}

	private async Task<ChannelOutcome?> PrivateMessageRefusalAsync(PmChannel pm, Connection actor,
		bool mayMessageAnyone, bool recipientSilenced, CancellationToken ct)
	{
		if (!mayMessageAnyone)
		{
			var relationships = await Context.Relationships.ListAsync(pm.Owner.Record, ct);
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