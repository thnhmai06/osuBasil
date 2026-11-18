using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Shared.Events;
using Basil.Application.Users.Persistence;
using Basil.Domain.Chat;

namespace Basil.Application.Chat.Objects;

public sealed record ChannelContext(
	TimeProvider Clock,
	IEventPublisher<ChannelEvent> Events,
	GeneralChannelRegistry Registry,
	IRelationshipRepository Relationships,
	IChannelRepository Channels)
{
	public GeneralChannel? OpenGeneral(GeneralChannelRecord record)
	{
		var channel = new GeneralChannel(record, this);
		if (!Registry.Add(channel)) return null;
		Events.Publish(new ChannelOpened(channel));
		return channel;
	}
}