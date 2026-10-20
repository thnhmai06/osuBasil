using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Servers;

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
	/// <param name="channel">The channel to create.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, or NameTaken when a channel with that name is already open.</returns>
	/// <remarks>Requires <see cref="Permissions.ServerConfigure" />.</remarks>
	public async Task<ChannelManageResult> CreateAsync(GeneralChannel channel, CancellationToken ct = default)
	{
		var context = _scoped.Server.Context.Session;
		if (_scoped.By.Session.Check(Permissions.ServerConfigure, context.Clock.GetUtcNow()) is not Access.Allowed)
			return ChannelManageResult.NotAuthorized;
		if (_scoped.Server.OpenChannel(channel) is null) return ChannelManageResult.NameTaken;
		await context.Chat.Channels.CreateOrUpdateAsync(channel, ct);
		return ChannelManageResult.Ok;
	}
}
