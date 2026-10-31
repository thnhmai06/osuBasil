using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Common.Events;
using Basil.Application.Users.Persistence;
using Basil.Domain.Chat;

namespace Basil.Application.Chat.Objects;

/// <summary>The ports through which a channel performs its behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The publisher for channel events.</param>
/// <param name="Registry">The open general channels.</param>
/// <param name="Relationships">The relationships that control private messages.</param>
/// <param name="Channels">The stored general channels.</param>
public sealed record ChannelContext(
	TimeProvider Clock,
	IEventPublisher<ChannelEvent> Events,
	GeneralChannelRegistry Registry,
	IRelationshipRepository Relationships,
	IChannelRepository Channels)
{
	/// <summary>Opens a general channel at the server's own request; no rights are needed.</summary>
	/// <param name="record">The channel record to open.</param>
	/// <returns>The opened channel, or <see langword="null" /> when a channel with that name is already open.</returns>
	public GeneralChannel? OpenGeneral(GeneralChannelRecord record)
	{
		var channel = new GeneralChannel(record, this);
		if (!Registry.Add(channel)) return null;
		Events.Publish(new ChannelOpened(channel));
		return channel;
	}
}