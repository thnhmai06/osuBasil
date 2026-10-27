using Basil.Application.Common;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Runtime;

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
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome; NameTaken when a channel with that name is already open.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ServerConfigure" />.</remarks>
	public async Task<Result<ServerOutcome>> CreateAsync(GeneralChannelRecord record,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Needs(UserPermissions.ServerConfigure) is { } missing) return Result<ServerOutcome>.Denied(missing);

		var chat = _scoped.Server.Context.Chat;
		if (_scoped.Server.OpenChannel(record) is null) return ServerOutcome.NameTaken;
		await chat.Channels.CreateOrUpdateAsync(record, cancellationToken);
		return ServerOutcome.Ok;
	}
}