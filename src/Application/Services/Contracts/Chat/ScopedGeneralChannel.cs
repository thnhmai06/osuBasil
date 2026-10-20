using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>A general channel scoped to one connection.</summary>
public sealed class ScopedGeneralChannel
{
	/// <summary>The general channel this scope is over.</summary>
	public GeneralChannelSession Channel { get; }

	/// <summary>The connection this scope is for.</summary>
	public Connection By { get; }

	/// <summary>Creates a general-channel scope for a connection.</summary>
	/// <param name="channel">The general channel to scope.</param>
	/// <param name="by">The connection acting on the channel.</param>
	internal ScopedGeneralChannel(GeneralChannelSession channel, Connection by)
	{
		Channel = channel;
		By = by;
	}

	/// <summary>Changes the settings of this general channel.</summary>
	/// <param name="change">The settings to change.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, or Closed when the channel has closed.</returns>
	/// <remarks>
	///     Requires <see cref="Permissions.ServerConfigure" />. Members who may no longer read the channel are removed;
	///     one <see cref="ChannelConfigured" /> reports the change and removed members.
	/// </remarks>
	/// <exception cref="ArgumentException">A permission or the topic in <paramref name="change" /> is invalid.</exception>
	public async Task<ChannelManageResult> ConfigureAsync(GeneralChannelChange change, CancellationToken ct = default)
	{
		var now = Channel.Context.Clock.GetUtcNow();
		if (By.Session.Check(Permissions.ServerConfigure, now) is not Access.Allowed)
			return ChannelManageResult.NotAuthorized;

		using (Channel.Enter())
		{
			if (Channel.IsClosed) return ChannelManageResult.Closed;
			var target = Channel.Channel;

			if (change.ReadPrivileges is { } readPrivileges) readPrivileges.ThrowIfUndefined();
			if (change.WritePrivileges is { } writePrivileges) writePrivileges.ThrowIfUndefined();
			change.Topic?.ThrowIfHasNul();

			if (change.Topic is { } topic) target.Topic = topic;
			if (change.ReadPrivileges is { } read) target.ReadPrivileges = read;
			if (change.WritePrivileges is { } write) target.WritePrivileges = write;
			if (change.AutoJoin is { } autoJoin) target.AutoJoin = autoJoin;
			if (change.Visible is { } visible) target.Visible = visible;

			var removed = Channel.Members.Where(member => !ChannelRules.CanRead(Channel, member, now)).ToArray();
			foreach (var member in removed)
				Channel.MutableMembers.Remove(member);
			Channel.Context.Events.Emit(new ChannelConfigured(Channel, change, removed));
		}

		await Channel.Context.Channels.CreateOrUpdateAsync(Channel.Channel, ct);
		return ChannelManageResult.Ok;
	}

	/// <summary>Closes and deletes this general channel.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>Ok or NotAuthorized.</returns>
	/// <remarks>Requires <see cref="Permissions.ServerConfigure" />; announces <see cref="ChannelClosed" />.</remarks>
	public async Task<ChannelManageResult> DeleteAsync(CancellationToken ct = default)
	{
		if (By.Session.Check(Permissions.ServerConfigure, Channel.Context.Clock.GetUtcNow()) is not Access.Allowed)
			return ChannelManageResult.NotAuthorized;

		Channel.Close();
		await Channel.Context.Channels.DeleteAsync(Channel.Channel, ct);
		return ChannelManageResult.Ok;
	}
}