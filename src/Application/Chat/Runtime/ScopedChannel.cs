using Basil.Application.Common;
using Basil.Application.Users.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Runtime;

/// <summary>A chat channel scoped to one connection.</summary>
public sealed class ScopedChannel : Scoped<UserPermissions, Connection>
{
	/// <summary>Creates a channel scope for a connection.</summary>
	/// <param name="channel">The channel to scope.</param>
	/// <param name="by">The connection acting on the channel.</param>
	internal ScopedChannel(Channel channel, Connection by) : base(by,
		by.EffectivePermissions(channel.Context.Clock.GetUtcNow()))
	{
		Channel = channel;
		General = channel is GeneralChannel general ? new ScopedGeneralChannel(general, by) : null;
	}

	/// <summary>The channel this scope is over.</summary>
	public Channel Channel { get; }

	/// <summary>The general-channel operations, or <see langword="null" /> for another kind of channel.</summary>
	public ScopedGeneralChannel? General { get; }

	/// <summary>Adds the scoped connection to the channel.</summary>
	/// <returns>Ok, or Closed, AccessDenied or AlreadyMember.</returns>
	/// <remarks>
	///     The connection must be allowed to read the channel. A closed connection of a kind limited to one channel
	///     membership per user is replaced, and the join event reports it.
	/// </remarks>
	public Result<ChannelOutcome> Join()
	{
		using var scope = Channel.Enter();
		if (Channel.IsClosed) return ChannelOutcome.Closed;
		if (!Channel.CanRead(By, Channel.Context.Clock.GetUtcNow())) return ChannelOutcome.AccessDenied;
		if (Channel.Members.Contains(By)) return ChannelOutcome.AlreadyMember;

		Connection? replaced = null;
		if (!By.Type.AllowsMany())
		{
			replaced = Channel.Members.FirstOrDefault(member =>
				member.Record.Equals(By.Record) && member.Type == By.Type);
			if (replaced is not null)
			{
				if (replaced.IsOpen) return ChannelOutcome.AlreadyMember;
				Channel.MutableMembers.Remove(replaced);
			}
		}

		Channel.MutableMembers.Add(By);
		Channel.Context.Events.Publish(new ChannelMemberJoined(Channel, By, replaced));
		return ChannelOutcome.Ok;
	}

	/// <summary>Removes the scoped connection from the channel.</summary>
	/// <returns>Ok, or NotMember.</returns>
	/// <remarks>A successful part announces <see cref="ChannelMemberParted" />.</remarks>
	public Result<ChannelOutcome> Part()
	{
		using var scope = Channel.Enter();
		if (!Channel.MutableMembers.Remove(By)) return ChannelOutcome.NotMember;
		Channel.Context.Events.Publish(new ChannelMemberParted(Channel, By, false));
		return ChannelOutcome.Ok;
	}

	/// <summary>Posts a message to the channel.</summary>
	/// <param name="text">The text of the message.</param>
	/// <param name="notice">Whether the message is a notice, which never triggers an automatic reply.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, or Closed, Empty, InvalidText, NotMember, AccessDenied, Blocked or TargetSilenced; a denial that names the
	///     missing
	///     permissions when the sender lacks the permission to post.
	/// </returns>
	/// <remarks>
	///     Messages longer than <see cref="Channel.MaxMessageLength" /> characters are cut. A private message is a
	///     post into the recipient's channel; their away reply is carried by the same <see cref="ChannelMessagePosted" />.
	///     Posting requires <see cref="UserPermissions.ChatPost" />, or <see cref="UserPermissions.ChatPostPrivate" /> for a
	///     private message. Blocks and friends-only settings are bypassed with
	///     <see cref="UserPermissions.ChatManageMessageAnyone" />.
	/// </remarks>
	public async Task<Result<ChannelOutcome>> PostAsync(string text, bool notice = false,
		CancellationToken cancellationToken = default)
	{
		if (Channel.IsClosed) return ChannelOutcome.Closed;

		if (Require(Channel is PmChannel ? UserPermissions.ChatPostPrivate : UserPermissions.ChatPost) is { } missing)
			return Result<ChannelOutcome>.Denied(missing);

		if (string.IsNullOrEmpty(text)) return ChannelOutcome.Empty;
		if (!Message.IsValidContent(text)) return ChannelOutcome.InvalidText;
		if (Channel is not PmChannel && !Channel.Members.Contains(By)) return ChannelOutcome.NotMember;

		var now = Channel.Context.Clock.GetUtcNow();
		if (!Channel.CanWrite(By, now)) return ChannelOutcome.AccessDenied;

		if (Channel is PmChannel pm && await PrivateMessageRefusalAsync(pm, now, cancellationToken) is { } refusal)
			return refusal;

		Channel.PostMessage(By.Record, text, notice, now, By);
		return ChannelOutcome.Ok;
	}

	private async Task<ChannelOutcome?> PrivateMessageRefusalAsync(PmChannel pm, DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		if (!Rights.Allows(UserPermissions.ChatManageMessageAnyone))
		{
			var relationships = await Channel.Context.Relationships.ListAsync(pm.Owner.Record, cancellationToken);
			var blocked = relationships.Any(relationship =>
				relationship.Type == RelationshipType.Block && relationship.Target.Equals(By.Record));
			var friend = relationships.Any(relationship =>
				relationship.Type == RelationshipType.Friend && relationship.Target.Equals(By.Record));
			if (blocked || (pm.Owner.PmPrivate && !friend)) return ChannelOutcome.Blocked;
		}

		if (!pm.Owner.Record.IsBasilBot() && !pm.Owner.EffectivePermissions(now).Allows(UserPermissions.ChatPostPrivate))
			return ChannelOutcome.TargetSilenced;

		return null;
	}
}