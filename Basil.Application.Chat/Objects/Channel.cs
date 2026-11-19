using Basil.Application.Chat.Capabilities;
using Basil.Application.Chat.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;
using Session = Basil.Application.Users.Objects.Sessions.Session;

namespace Basil.Application.Chat.Objects;

public abstract class Channel
{
	public const int MaxMessageLength = 2000;

	private readonly ConcurrentSet<Session> _members = [];
	private readonly Lock _sync = new();
	private volatile bool _closed;

	internal Channel(ChannelContext context)
	{
		Context = context;
	}

	internal ChannelContext Context { get; }

	public abstract string Name { get; }

	public abstract string Topic { get; }

	public IReadOnlySet<Session> Members => _members;

	internal ISet<Session> MutableMembers => _members;

	public bool IsClosed
	{
		get => _closed;
		internal set => _closed = value;
	}

	internal abstract bool CanRead(Session connection, UserPrivileges privileges);

	internal abstract bool CanWrite(Session connection, UserPrivileges privileges);

	public ScopedChannel ScopeTo(Session actor)
	{
		return new ScopedChannel(this, actor);
	}

	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	public async Task<IReadOnlyList<Session>> RecipientsAsync(ChannelMessagePosted posted,
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

	public void PostMessage(UserRecord author, string text, bool notice, DateTimeOffset now, Session? actor,
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

	internal ChannelJoinResult Join(Session connection, UserPrivileges privileges)
	{
		using var scope = Enter();
		if (IsClosed) return ChannelJoinResult.Closed;
		if (!CanRead(connection, privileges)) return ChannelJoinResult.AccessDenied;
		if (Members.Contains(connection)) return ChannelJoinResult.AlreadyMember;

		Session? replaced = null;
		if (connection.Type.IsExclusive)
		{
			replaced = Members.FirstOrDefault(member =>
				member.Record.Equals(connection.Record) && member.Type == connection.Type);
			if (replaced is not null)
			{
				if (replaced.IsOpen) return ChannelJoinResult.AlreadyMember;
				MutableMembers.Remove(replaced);
			}
		}

		MutableMembers.Add(connection);
		Context.Events.Publish(new ChannelMemberJoined(this, connection, replaced));
		return ChannelJoinResult.Ok;
	}

	internal ChannelJoinResult Part(Session connection)
	{
		using var scope = Enter();
		if (!MutableMembers.Remove(connection)) return ChannelJoinResult.NotMember;
		Context.Events.Publish(new ChannelMemberParted(this, connection, false));
		return ChannelJoinResult.Ok;
	}

	internal async Task<ChannelJoinResult> PostAsync(Session actor, UserPrivileges privileges,
		bool? recipientSilenced, string text, bool notice, CancellationToken ct)
	{
		if (IsClosed) return ChannelJoinResult.Closed;
		if (string.IsNullOrEmpty(text)) throw new EmptyMessageException();
		if (!Message.IsValidContent(text)) throw new InvalidMessageException();
		if (this is not PmChannel && !Members.Contains(actor)) return ChannelJoinResult.NotMember;

		var now = Context.Clock.GetUtcNow();
		if (actor.User.Record.Value.IsSilenced(now)) return ChannelJoinResult.Silenced;
		if (!CanWrite(actor, privileges)) return ChannelJoinResult.AccessDenied;

		var mayMessageAnyone = privileges.Allows(UserPrivileges.Moderator);
		if (this is PmChannel pm && recipientSilenced is { } recipient &&
		    await PrivateMessageRefusalAsync(pm, actor, mayMessageAnyone, recipient, ct) is { } refusal)
			return refusal;

		if (IsClosed) return ChannelJoinResult.Closed;

		PostMessage(actor.Record, text, notice, now, actor, mayMessageAnyone);
		return ChannelJoinResult.Ok;
	}

	private async Task<ChannelJoinResult?> PrivateMessageRefusalAsync(PmChannel pm, Session actor,
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
			if (blocked || (pm.Owner.PmPrivate && !friend)) return ChannelJoinResult.Blocked;
		}

		if (pm.Owner.Record.Id != BasilBot.Id && recipientSilenced)
			return ChannelJoinResult.TargetSilenced;

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