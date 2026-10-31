using Basil.Application.Chat.Objects;
using Basil.Application.Common.Common;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

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
	/// <remarks>Requires <see cref="UserPermissions.ChatManageChannels" />.</remarks>
	public async Task<Result<ServerOutcome>> CreateAsync(GeneralChannelRecord record,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Needs(UserPermissions.ChatManageChannels) is { } missing)
			return Result<ServerOutcome>.Denied(missing);

		var chat = _scoped.Server.Context.Chat;
		if (_scoped.Server.OpenChannel(record) is null) return ServerOutcome.NameTaken;
		await chat.Channels.CreateOrUpdateAsync(record, cancellationToken);
		return ServerOutcome.Ok;
	}

	/// <summary>Finds the general channel to manage under a name.</summary>
	/// <param name="name">The channel name.</param>
	/// <returns>
	///     The open channel, NotFound when no open channel has that name, or a denial that names the missing permissions.
	/// </returns>
	/// <remarks>
	///     Requires <see cref="UserPermissions.ChatManageChannels" />. The name is looked up only after the right is met,
	///     so a connection without it is refused the same way whether or not the channel exists.
	/// </remarks>
	public (Result<ChannelOutcome> Result, GeneralChannel? Channel) Find(string name)
	{
		if (_scoped.Needs(UserPermissions.ChatManageChannels) is { } missing)
			return (Result<ChannelOutcome>.Denied(missing), null);

		return _scoped.Server.Context.Chat.Registry.Find(name) is { } channel
			? (ChannelOutcome.Ok, channel)
			: (ChannelOutcome.NotFound, null);
	}
}