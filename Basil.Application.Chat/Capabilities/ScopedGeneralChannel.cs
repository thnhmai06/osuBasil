using Basil.Application.Chat.Objects;
using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Capabilities;

public readonly struct ScopedGeneralChannel
{
	public Session Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	internal ScopedGeneralChannel(GeneralChannel channel, Session actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(channel.Context.Clock.GetUtcNow());
		Channel = channel;
	}

	public GeneralChannel Channel { get; }

	public async Task<ChannelJoinResult> ConfigureAsync(GeneralChannelChange change)
	{
		if (Lacks(UserPrivileges.Moderator)) return ChannelJoinResult.Denied;

		var now = Channel.Context.Clock.GetUtcNow();
		return await Channel.ConfigureAsync(change, member => member.EffectivePrivileges(now));
	}

	public async Task<ChannelJoinResult> DeleteAsync()
	{
		if (Lacks(UserPrivileges.Moderator)) return ChannelJoinResult.Denied;

		return await Channel.DeleteAsync();
	}
}