using Basil.Application.Chat.Objects;
using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>General-channel operations scoped to one connection.</summary>
public readonly struct ScopedServerChannels
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
	/// <returns>The outcome; NameTaken when a channel with that name is already open.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Moderator" />.</remarks>
	public async Task<ServerOutcome> CreateAsync(GeneralChannelRecord record)
	{
		if (_scoped.Lacks(UserPrivileges.Moderator))
			return ServerOutcome.Denied;

		return await _scoped.Server.Channels.CreateAsync(record);
	}

	/// <summary>Finds the general channel to manage under a name.</summary>
	/// <param name="name">The channel name.</param>
	/// <returns>
	///     The open channel, NotFound when no open channel has that name, or Denied.
	/// </returns>
	/// <remarks>
	///     Requires <see cref="UserPrivileges.Moderator" />. The name is looked up only after the privilege is met,
	///     so a connection without it is refused the same way whether or not the channel exists.
	/// </remarks>
	public (ChannelOutcome Result, GeneralChannel? Channel) FindGeneral(string name)
	{
		if (_scoped.Lacks(UserPrivileges.Moderator))
			return (ChannelOutcome.Denied, null);

		return _scoped.Server.Channels.FindGeneral(name);
	}
}