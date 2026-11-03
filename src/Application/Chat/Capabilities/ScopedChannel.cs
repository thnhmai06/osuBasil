using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Capabilities;

/// <summary>A chat channel scoped to one connection.</summary>
public sealed class ScopedChannel : Scoped<UserPermissions, Connection>
{
	/// <summary>Creates a channel scope for a connection.</summary>
	/// <param name="channel">The channel to scope.</param>
	/// <param name="actor">The connection acting on the channel.</param>
	internal ScopedChannel(Channel channel, Connection actor) : base(actor,
		actor.EffectivePermissions(channel.Context.Clock.GetUtcNow()))
	{
		Channel = channel;
		General = channel is GeneralChannel general ? new ScopedGeneralChannel(general, actor) : null;
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
		if (!Channel.CanRead(Actor, Channel.Context.Clock.GetUtcNow())) return ChannelOutcome.AccessDenied;
		if (Channel.Members.Contains(Actor)) return ChannelOutcome.AlreadyMember;

		Connection? replaced = null;
		if (!Actor.Type.AllowsMany())
		{
			replaced = Channel.Members.FirstOrDefault(member =>
				member.Record.Equals(Actor.Record) && member.Type == Actor.Type);
			if (replaced is not null)
			{
				if (replaced.IsOpen) return ChannelOutcome.AlreadyMember;
				Channel.MutableMembers.Remove(replaced);
			}
		}

		Channel.MutableMembers.Add(Actor);
		Channel.Context.Events.Publish(new ChannelMemberJoined(Channel, Actor, replaced));
		return ChannelOutcome.Ok;
	}

	/// <summary>Removes the scoped connection from the channel.</summary>
	/// <returns>Ok, or NotMember.</returns>
	/// <remarks>A successful part announces <see cref="ChannelMemberParted" />.</remarks>
	public Result<ChannelOutcome> Part()
	{
		using var scope = Channel.Enter();
		if (!Channel.MutableMembers.Remove(Actor)) return ChannelOutcome.NotMember;
		Channel.Context.Events.Publish(new ChannelMemberParted(Channel, Actor, false));
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
		if (Channel is not PmChannel && !Channel.Members.Contains(Actor)) return ChannelOutcome.NotMember;

		var now = Channel.Context.Clock.GetUtcNow();
		if (!Channel.CanWrite(Actor, now)) return ChannelOutcome.AccessDenied;

		if (Channel is PmChannel pm && await PrivateMessageRefusalAsync(pm, now, cancellationToken) is { } refusal)
			return refusal;

		if (Channel.IsClosed) return ChannelOutcome.Closed;

		Channel.PostMessage(Actor.Record, text, notice, now, Actor);
		return ChannelOutcome.Ok;
	}

	private async Task<ChannelOutcome?> PrivateMessageRefusalAsync(PmChannel pm, DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		if (!Rights.Allows(UserPermissions.ChatManageMessageAnyone))
		{
			var relationships = await Channel.Context.Relationships.ListAsync(pm.Owner.Record, cancellationToken);
			var blocked = relationships.Any(relationship =>
				relationship.Type == RelationshipType.Block && relationship.Target.Equals(Actor.Record));
			var friend = relationships.Any(relationship =>
				relationship.Type == RelationshipType.Friend && relationship.Target.Equals(Actor.Record));
			if (blocked || (pm.Owner.PmPrivate && !friend)) return ChannelOutcome.Blocked;
		}

		if (!pm.Owner.Record.IsFriendOfEveryone() &&
		    !pm.Owner.EffectivePermissions(now).Allows(UserPermissions.ChatPostPrivate))
			return ChannelOutcome.TargetSilenced;

		return null;
	}
}