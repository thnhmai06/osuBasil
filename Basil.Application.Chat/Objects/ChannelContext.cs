using System.Runtime.CompilerServices;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Shared.Events;
using Basil.Application.Sessions.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Chat;

namespace Basil.Application.Chat.Objects;

public sealed record ChannelContext(
	TimeProvider Clock,
	IEventPublisher<ChannelEvent> Events,
	GeneralChannelRegistry Registry,
	RelationshipRegistry Relationships,
	IChannelRepository Channels)
{
	// The closed sessions whose user's quit was announced: their later parts show no PART.
	internal ConditionalWeakTable<Session, object> Quitted { get; } = new();

	public GeneralChannel? OpenGeneral(GeneralChannelRecord record)
	{
		var channel = new GeneralChannel(record, this);
		if (!Registry.Add(channel)) return null;
		Events.Publish(new ChannelOpened(channel));
		return channel;
	}
}