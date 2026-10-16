using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Chat;

/// <summary>Opens, creates, configures and deletes the general channels.</summary>
internal sealed class GeneralChannelService(
	IGeneralChannelRegistry generalChannels,
	IChannelRepository channels,
	ChannelMembershipService members,
	ChannelEventStream events,
	TimeProvider time) : IGeneralChannelService
{
	/// <inheritdoc />
	public GeneralChannelSession? Open(GeneralChannel channel)
	{
		var session = new GeneralChannelSession(channel);
		if (!generalChannels.Add(session)) return null;
		events.Emit(new ChannelOpened(session));
		return session;
	}

	/// <inheritdoc />
	public async Task<ChannelManageResult> CreateAsync(GeneralChannel channel, Connection by,
		CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.Allows(by, Permissions.ServerConfigure, time.GetUtcNow()))
			return ChannelManageResult.NotAuthorized;
		if (Open(channel) is null) return ChannelManageResult.NameTaken;
		await channels.CreateOrUpdateAsync(channel, cancellationToken).ConfigureAwait(false);
		return ChannelManageResult.Ok;
	}

	/// <inheritdoc />
	public async Task<ChannelManageResult> ConfigureAsync(GeneralChannelSession channel, GeneralChannelChange change,
		Connection by, CancellationToken cancellationToken = default)
	{
		var now = time.GetUtcNow();
		if (!PermissionRules.Allows(by, Permissions.ServerConfigure, now))
			return ChannelManageResult.NotAuthorized;

		Connection[] removed;
		using (channel.Enter())
		{
			if (channel.IsClosed) return ChannelManageResult.Closed;
			var target = channel.Channel;

			if (change.ReadPermissions is { } r) r.ThrowIfUndefined();
			if (change.WritePermissions is { } w) w.ThrowIfUndefined();
			change.Topic?.ThrowIfHasNul();

			if (change.Topic is { } topic) target.Topic = topic;
			if (change.ReadPermissions is { } read) target.ReadPermissions = read;
			if (change.WritePermissions is { } write) target.WritePermissions = write;
			if (change.AutoJoin is { } autoJoin) target.AutoJoin = autoJoin;
			if (change.Visible is { } visible) target.Visible = visible;

			removed = [.. channel.Members.Where(member => !ChannelRules.CanRead(channel, member, now))];
			foreach (var member in removed)
				channel.MutableMembers.Remove(member);
			events.Emit(new ChannelConfigured(channel, change, removed));
		}

		await channels.CreateOrUpdateAsync(channel.Channel, cancellationToken).ConfigureAwait(false);
		return ChannelManageResult.Ok;
	}

	/// <inheritdoc />
	public async Task<ChannelManageResult> DeleteAsync(GeneralChannelSession channel, Connection by,
		CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.Allows(by, Permissions.ServerConfigure, time.GetUtcNow()))
			return ChannelManageResult.NotAuthorized;
		members.Close(channel);
		await channels.DeleteAsync(channel.Channel, cancellationToken).ConfigureAwait(false);
		return ChannelManageResult.Ok;
	}
}