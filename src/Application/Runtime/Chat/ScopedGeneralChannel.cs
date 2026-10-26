using Basil.Application.Runtime.Common;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Chat;

/// <summary>A general channel scoped to one connection.</summary>
public sealed class ScopedGeneralChannel : Scoped<UserPermissions>
{
	/// <summary>Creates a general-channel scope for a connection.</summary>
	/// <param name="channel">The general channel to scope.</param>
	/// <param name="by">The connection acting on the channel.</param>
	internal ScopedGeneralChannel(GeneralChannel channel, Connection by) : base(by, by.User.EffectivePermissions(channel.Context.Clock.GetUtcNow()))
	{
		Channel = channel;
	}

	/// <summary>The general channel this scope is over.</summary>
	public GeneralChannel Channel { get; }

	/// <summary>Changes the settings of this general channel.</summary>
	/// <param name="change">The settings to change.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, a denial that names the missing permissions, or Closed when the channel has closed.</returns>
	/// <remarks>
	///     Requires <see cref="UserPermissions.ServerConfigure" />. Members who may no longer read the channel are removed;
	///     one <see cref="ChannelConfigured" /> reports the change and removed members.
	/// </remarks>
	/// <exception cref="ArgumentException">A permission or the topic in <paramref name="change" /> is invalid.</exception>
	public async Task<Result<ChannelOutcome>> ConfigureAsync(GeneralChannelChange change,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.ServerConfigure) is { } missing) return Result<ChannelOutcome>.Denied(missing);

		using (Channel.Enter())
		{
			if (Channel.IsClosed) return ChannelOutcome.Closed;
			var target = Channel.Record.Value;

			if (change.ReadPrivileges is { } readPrivileges) readPrivileges.ThrowIfUndefined();
			if (change.WritePrivileges is { } writePrivileges) writePrivileges.ThrowIfUndefined();
			change.Topic?.ThrowIfHasNul();

			if (change.Topic is { } topic) target.Topic = topic;
			if (change.ReadPrivileges is { } read) target.ReadPrivileges = read;
			if (change.WritePrivileges is { } write) target.WritePrivileges = write;
			if (change.AutoJoin is { } autoJoin) target.AutoJoin = autoJoin;
			if (change.Visible is { } visible) target.Visible = visible;

			var now = Channel.Context.Clock.GetUtcNow();
			var removed = Channel.Members.Where(member => !Channel.CanRead(member, now)).ToArray();
			foreach (var member in removed)
				Channel.MutableMembers.Remove(member);
			Channel.Context.Events.Publish(new ChannelConfigured(Channel, change, removed));
		}

		await Channel.Context.Channels.CreateOrUpdateAsync(Channel.Record, cancellationToken);
		return ChannelOutcome.Ok;
	}

	/// <summary>Closes and deletes this general channel.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, or a denial that names the missing permissions.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerConfigure" />; announces <see cref="ChannelClosed" />.</remarks>
	public async Task<Result<ChannelOutcome>> DeleteAsync(CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.ServerConfigure) is { } missing) return Result<ChannelOutcome>.Denied(missing);

		Channel.Close();
		await Channel.Context.Channels.DeleteAsync(Channel.Record, cancellationToken);
		return ChannelOutcome.Ok;
	}
}