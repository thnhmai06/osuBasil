using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Capabilities;

/// <summary>A chat channel scoped to one connection.</summary>
public readonly struct ScopedChannel
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the permissions the actor held when the scope was created.</summary>
	public UserPermissions Rights { get; }

	/// <summary>Checks whether the actor holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPermissions? Require(UserPermissions required)
	{
		return ScopedRights.Require(Rights, required);
	}

	/// <summary>Creates a channel scope for a connection.</summary>
	/// <param name="channel">The channel to scope.</param>
	/// <param name="actor">The connection acting on the channel.</param>
	internal ScopedChannel(Channel channel, Connection actor)
	{
		Actor = actor;
		Rights = actor.EffectivePermissions(channel.Context.Clock.GetUtcNow());
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
	///     The connection must be allowed to read the channel. A closed connection of a kind that holds one channel
	///     place per user is replaced, and the join event reports it.
	/// </remarks>
	public Result<ChannelOutcome> Join()
	{
		return Channel.Join(Actor, Rights);
	}

	/// <summary>Removes the scoped connection from the channel.</summary>
	/// <returns>Ok, or NotMember.</returns>
	/// <remarks>A successful part announces <see cref="ChannelMemberParted" />.</remarks>
	public Result<ChannelOutcome> Part()
	{
		return Channel.Part(Actor);
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

		var recipientPermissions = Channel is PmChannel pm
			? pm.Owner.EffectivePermissions(Channel.Context.Clock.GetUtcNow())
			: (UserPermissions?)null;
		return await Channel.PostAsync(Actor, Rights, recipientPermissions, text, notice, cancellationToken);
	}
}