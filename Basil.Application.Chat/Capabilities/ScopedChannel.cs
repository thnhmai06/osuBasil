using Basil.Application.Chat.Objects;
using Basil.Application.Shared.Privileges;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Capabilities;

public readonly struct ScopedChannel
{
	public Session Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	internal ScopedChannel(Channel channel, Session actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(channel.Context.Clock.GetUtcNow());
		Channel = channel;
		General = channel is GeneralChannel general ? new ScopedGeneralChannel(general, actor) : null;
	}

	public Channel Channel { get; }

	public ScopedGeneralChannel? General { get; }

	public ChannelJoinResult Join()
	{
		return Channel.Join(Actor, Privileges);
	}

	public ChannelJoinResult Part()
	{
		return Channel.Part(Actor);
	}

	public async Task<ChannelJoinResult> PostAsync(string text, bool notice = false, bool action = false,
		CancellationToken ct = default)
	{
		if (Channel.IsClosed) return ChannelJoinResult.Closed;

		if (Lacks(UserPrivileges.Player))
			return ChannelJoinResult.Denied;

		var recipientSilenced = Channel is PmChannel pm
			? pm.Owner.Record.Value.IsSilenced(Channel.Context.Clock.GetUtcNow())
			: (bool?)null;
		return await Channel.PostAsync(Actor, Privileges, recipientSilenced, text, notice, action, ct);
	}
}