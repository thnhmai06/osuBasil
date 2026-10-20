using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Permissions;

namespace Basil.Application.Objects.Servers;

/// <summary>General-channel operations scoped to one connection.</summary>
public sealed class ScopedServerChannels
{
	private readonly ScopedServer _scoped;

	/// <summary>Creates channel operations for a server scope.</summary>
	/// <param name="scoped">The server scope.</param>
	internal ScopedServerChannels(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Creates and opens a general channel.</summary>
	/// <param name="record">The channel to create.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, or NameTaken when a channel with that name is already open.</returns>
	/// <remarks>Requires <see cref="Permissions.ServerConfigure" />.</remarks>
	public async Task<ChannelManageResult> CreateAsync(GeneralChannelRecord record, CancellationToken ct = default)
	{
		var context = _scoped.Server.Context.Session;
		if (_scoped.By.User.Check(Permissions.ServerConfigure, context.Clock.GetUtcNow()) is not Access.Allowed)
			return ChannelManageResult.NotAuthorized;
		if (_scoped.Server.OpenChannel(record) is null) return ChannelManageResult.NameTaken;
		await context.Chat.Channels.CreateOrUpdateAsync(record, ct);
		return ChannelManageResult.Ok;
	}
}