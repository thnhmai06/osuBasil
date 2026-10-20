using System.Threading.Channels;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Sessions;

namespace Basil.Application.Services.Chat;

/// <summary>Opens general channels and manages automatic channel membership.</summary>
internal sealed class ChannelService(ChannelContext context, ChannelEventStream events) : IChannelService
{
	/// <inheritdoc />
	public ChannelReader<ChannelEvent> Events => events.Reader;

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
