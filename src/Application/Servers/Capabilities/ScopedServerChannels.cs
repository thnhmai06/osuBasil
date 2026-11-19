using Basil.Application.Chat.Objects;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Game.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerChannels
{
	private readonly ScopedServer _scoped;

	internal ScopedServerChannels(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	public async Task<ServerOutcome> CreateAsync(GeneralChannelRecord record)
	{
		if (_scoped.Lacks(UserPrivileges.Moderator))
			return ServerOutcome.Denied;

		return await _scoped.Server.Channels.CreateAsync(record);
	}

	public (ChannelOutcome Result, GeneralChannel? Channel) FindGeneral(string name)
	{
		if (_scoped.Lacks(UserPrivileges.Moderator))
			return (ChannelOutcome.Denied, null);

		return _scoped.Server.Channels.FindGeneral(name);
	}
}