using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Sessions;

namespace Basil.Application.Services.Chat;

/// <summary>Opens general channels and manages automatic channel membership.</summary>
internal sealed class ChannelService(ChannelContext context) : IChannelService
{
	/// <inheritdoc />
	public void JoinAutoChannels(Connection by)
	{
		foreach (var channel in context.Registry.All.Values)
			if (channel.Channel.AutoJoin)
				channel.ScopeTo(by).Join();
	}

	/// <inheritdoc />
	public void PartAll(Connection by)
	{
		foreach (var channel in context.Registry.All.Values)
			channel.ScopeTo(by).Part();
	}
}