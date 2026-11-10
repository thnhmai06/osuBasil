using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Capabilities;

/// <summary>A general channel scoped to one connection.</summary>
public readonly struct ScopedGeneralChannel
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the privileges the actor held when the scope was created.</summary>
	public UserPrivileges Rights { get; }

	/// <summary>Checks whether the actor holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPrivileges? Require(UserPrivileges required)
	{
		return ScopedRights.Require(Rights, required);
	}

	/// <summary>Creates a general-channel scope for a connection.</summary>
	/// <param name="channel">The general channel to scope.</param>
	/// <param name="actor">The connection acting on the channel.</param>
	internal ScopedGeneralChannel(GeneralChannel channel, Connection actor)
	{
		Actor = actor;
		Rights = actor.EffectivePrivileges(channel.Context.Clock.GetUtcNow());
		Channel = channel;
	}

	/// <summary>The general channel this scope is over.</summary>
	public GeneralChannel Channel { get; }

	/// <summary>Changes the settings of this general channel.</summary>
	/// <param name="change">The settings to change.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, a denial that names the missing privileges, or Closed when the channel has closed.</returns>
	/// <remarks>
	///     Requires <see cref="UserPrivileges.Moderator" />. Members who may no longer read the channel are removed;
	///     one <see cref="ChannelConfigured" /> reports the change and removed members.
	/// </remarks>
	/// <exception cref="ArgumentException">A privilege or the topic in <paramref name="change" /> is invalid.</exception>
	public async Task<Result<ChannelOutcome>> ConfigureAsync(GeneralChannelChange change,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPrivileges.Moderator) is { } missing) return Result<ChannelOutcome>.Denied(missing);

		var now = Channel.Context.Clock.GetUtcNow();
		return await Channel.ConfigureAsync(change, member => member.EffectivePrivileges(now), cancellationToken);
	}

	/// <summary>Closes and deletes this general channel.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, or a denial that names the missing privileges.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Moderator" />; announces <see cref="ChannelClosed" />.</remarks>
	public async Task<Result<ChannelOutcome>> DeleteAsync(CancellationToken cancellationToken = default)
	{
		if (Require(UserPrivileges.Moderator) is { } missing) return Result<ChannelOutcome>.Denied(missing);

		return await Channel.DeleteAsync(cancellationToken);
	}
}