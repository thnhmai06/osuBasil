using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Shared;
using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Capabilities;

/// <summary>A chat channel scoped to one connection.</summary>
public readonly struct ScopedChannel
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the privileges the actor held when the scope was created.</summary>
	public UserPrivileges Privileges { get; }

	/// <summary>Checks whether the actor holds the privilege an action needs.</summary>
	/// <param name="required">The privilege the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	/// <summary>Creates a channel scope for a connection.</summary>
	/// <param name="channel">The channel to scope.</param>
	/// <param name="actor">The connection acting on the channel.</param>
	internal ScopedChannel(Channel channel, Connection actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(channel.Context.Clock.GetUtcNow());
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
	public ChannelOutcome Join()
	{
		return Channel.Join(Actor, Privileges);
	}

	/// <summary>Removes the scoped connection from the channel.</summary>
	/// <returns>Ok, or NotMember.</returns>
	/// <remarks>A successful part announces <see cref="ChannelMemberParted" />.</remarks>
	public ChannelOutcome Part()
	{
		return Channel.Part(Actor);
	}

	/// <summary>Posts a message to the channel.</summary>
	/// <param name="text">The text of the message.</param>
	/// <param name="notice">Whether the message is a notice, which never triggers an automatic reply.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, or Closed, Empty, InvalidText, NotMember, Silenced, AccessDenied, Blocked or TargetSilenced; Denied when the
	///     sender lacks the privilege to post.
	/// </returns>
	/// <remarks>
	///     Messages longer than <see cref="Channel.MaxMessageLength" /> characters are cut. A private message is a
	///     post into the recipient's channel; their away reply is carried by the same <see cref="ChannelMessagePosted" />.
	///     Posting requires <see cref="UserPrivileges.Player" /> and no silence. Blocks and friends-only settings are bypassed with
	///     <see cref="UserPrivileges.Moderator" />.
	/// </remarks>
	public async Task<ChannelOutcome> PostAsync(string text, bool notice = false,
		CancellationToken ct = default)
	{
		if (Channel.IsClosed) return ChannelOutcome.Closed;

		if (Lacks(UserPrivileges.Player))
			return ChannelOutcome.Denied;

		var recipientSilenced = Channel is PmChannel pm
			? pm.Owner.Record.Value.IsSilenced(Channel.Context.Clock.GetUtcNow())
			: (bool?)null;
		return await Channel.PostAsync(Actor, Privileges, recipientSilenced, text, notice, ct);
	}
}