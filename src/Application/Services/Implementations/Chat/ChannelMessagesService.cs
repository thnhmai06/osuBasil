using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Chat;

/// <summary>Posts messages to chat channels.</summary>
internal sealed class ChannelMessagesService(
	IRelationshipRepository relationships,
	ChannelEventStream events,
	TimeProvider time) : IChannelMessagesService
{
	/// <inheritdoc />
	public async Task<ChannelPostResult> PostAsync(ChannelSession channel, Connection by, string text,
		bool notice = false, CancellationToken cancellationToken = default)
	{
		if (channel.IsClosed) return ChannelPostResult.Closed;

		var now = time.GetUtcNow();
		var required = channel is PmChannelSession ? Permissions.PlayerPrivateMessage : Permissions.PlayerChat;
		switch (PermissionRules.Check(by, required, now))
		{
			case Access.NotGranted: return ChannelPostResult.NoWritePermission;
			case Access.Suspended: return ChannelPostResult.Silenced;
		}
		if (string.IsNullOrWhiteSpace(text)) return ChannelPostResult.Empty;
		if (channel is not PmChannelSession && !channel.Members.Contains(by)) return ChannelPostResult.NotMember;
		if (!ChannelRules.CanWrite(channel, by, now)) return ChannelPostResult.NoWritePermission;

		if (channel is PmChannelSession pm)
		{
			if (!PermissionRules.Allows(by, Permissions.ModeratorMessageAnyone, now))
			{
				var rels = await relationships.ListAsync(pm.Owner.User, cancellationToken);
				var blocked = rels.Any(r => r.Type == RelationshipType.Block && r.Target.Equals(by.User));
				var friend = rels.Any(r => r.Type == RelationshipType.Friend && r.Target.Equals(by.User));
				if (blocked || (pm.Owner.PmPrivate && !friend))
					return ChannelPostResult.Blocked;
			}

			if (pm.Owner.User.Value.Permissions.Allows(Permissions.PlayerPrivateMessage) &&
			    !PermissionRules.Effective(pm.Owner, now).Allows(Permissions.PlayerPrivateMessage))
				return ChannelPostResult.TargetSilenced;
		}

		var truncated = text.Length > ChannelSession.MaxMessageLength;
		var message = new Message(by.User, truncated ? text[..ChannelSession.MaxMessageLength] : text, now, notice);

		Message? awayReply = null;
		if (channel is PmChannelSession { Owner.AwayMessage: { } away } pmChannel
		    && !ReferenceEquals(by.Session, pmChannel.Owner)
		    && !notice)
			awayReply = new Message(pmChannel.Owner.User, away, now);

		events.Emit(new ChannelMessagePosted(channel, message, truncated, awayReply));
		return ChannelPostResult.Posted;
	}
}
