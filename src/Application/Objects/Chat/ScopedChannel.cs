using Basil.Application.Objects.Sessions;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Chat;

/// <summary>A chat channel scoped to one connection.</summary>
public sealed class ScopedChannel
{
	/// <summary>The channel this scope is over.</summary>
	public ChannelSession Channel { get; }

	/// <summary>The connection this scope is for.</summary>
	public Connection By { get; }

	/// <summary>The general-channel operations, or <see langword="null" /> for another kind of channel.</summary>
	public ScopedGeneralChannel? General { get; }

	/// <summary>Creates a channel scope for a connection.</summary>
	/// <param name="channel">The channel to scope.</param>
	/// <param name="by">The connection acting on the channel.</param>
	internal ScopedChannel(ChannelSession channel, Connection by)
	{
		Channel = channel;
		By = by;
		General = channel is GeneralChannelSession general ? new ScopedGeneralChannel(general, by) : null;
	}

	/// <summary>Adds the scoped connection to the channel.</summary>
	/// <returns>The outcome of the join.</returns>
	/// <remarks>
	///     The connection must be allowed to read the channel. A closed connection of a kind limited to one channel
	///     membership per user is replaced, and the join event reports it.
	/// </remarks>
	public ChannelJoinResult Join()
	{
		using var scope = Channel.Enter();
		if (Channel.IsClosed) return ChannelJoinResult.Closed;
		if (!ChannelRules.CanRead(Channel, By, Channel.Context.Clock.GetUtcNow()))
			return ChannelJoinResult.NoPermission;
		if (Channel.Members.Contains(By)) return ChannelJoinResult.AlreadyMember;

		Connection? replaced = null;
		if (!By.Type.AllowsMany())
		{
			replaced = Channel.Members.FirstOrDefault(member => member.User.Equals(By.User) && member.Type == By.Type);
			if (replaced is not null)
			{
				if (replaced.IsOpen) return ChannelJoinResult.AlreadyMember;
				Channel.MutableMembers.Remove(replaced);
			}
		}

		Channel.MutableMembers.Add(By);
		Channel.Context.Events.Publish(new ChannelMemberJoined(Channel, By, replaced));
		return ChannelJoinResult.Joined;
	}

	/// <summary>Removes the scoped connection from the channel.</summary>
	/// <returns>The outcome of the part.</returns>
	/// <remarks>A successful part announces <see cref="ChannelMemberParted" />.</remarks>
	public ChannelPartResult Part()
	{
		using var scope = Channel.Enter();
		if (!Channel.MutableMembers.Remove(By)) return ChannelPartResult.NotMember;
		Channel.Context.Events.Publish(new ChannelMemberParted(Channel, By, false));
		return ChannelPartResult.Parted;
	}

	/// <summary>Posts a message to the channel.</summary>
	/// <param name="text">The text of the message.</param>
	/// <param name="notice">Whether the message is a notice, which never triggers an automatic reply.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of the post.</returns>
	/// <remarks>
	///     Messages longer than <see cref="ChannelSession.MaxMessageLength" /> characters are cut. A private message is a
	///     post into the recipient's channel; their away reply is carried by the same <see cref="ChannelMessagePosted" />.
	///     Posting requires <see cref="Permissions.ChatPost" />, or <see cref="Permissions.ChatPostPrivate" /> for a
	///     private message. Blocks and friends-only settings are bypassed with <see cref="Permissions.ChatMessageAnyone" />.
	/// </remarks>
	public async Task<ChannelPostResult> PostAsync(string text, bool notice = false,
		CancellationToken cancellationToken = default)
	{
		if (Channel.IsClosed) return ChannelPostResult.Closed;

		var now = Channel.Context.Clock.GetUtcNow();
		var required = Channel is PmChannelSession ? Permissions.ChatPostPrivate : Permissions.ChatPost;
		switch (By.Session.Check(required, now))
		{
			case Access.NotGranted: return ChannelPostResult.NoWritePermission;
			case Access.Suspended: return ChannelPostResult.Silenced;
		}

		if (string.IsNullOrWhiteSpace(text)) return ChannelPostResult.Empty;
		if (Channel is not PmChannelSession && !Channel.Members.Contains(By)) return ChannelPostResult.NotMember;
		if (!ChannelRules.CanWrite(Channel, By, now)) return ChannelPostResult.NoWritePermission;

		if (Channel is PmChannelSession pm)
		{
			if (By.Session.Check(Permissions.ChatMessageAnyone, now) is not Access.Allowed)
			{
				var relationships = await Channel.Context.Relationships.ListAsync(pm.Owner.User, cancellationToken);
				var blocked = relationships.Any(relationship =>
					relationship.Type == RelationshipType.Block && relationship.Target.Equals(By.User));
				var friend = relationships.Any(relationship =>
					relationship.Type == RelationshipType.Friend && relationship.Target.Equals(By.User));
				if (blocked || (pm.Owner.PmPrivate && !friend)) return ChannelPostResult.Blocked;
			}

			if (pm.Owner.Check(Permissions.ChatPostPrivate, now) is Access.Suspended)
				return ChannelPostResult.TargetSilenced;
		}

		Channel.PostMessage(By.User, text, notice, now, By);
		return ChannelPostResult.Posted;
	}
}