using Basil.Application.Persistence.Chat;
using Basil.Application.Persistence.Users;
using Basil.Application.Runtime.Events;

namespace Basil.Application.Runtime.Chat;

/// <summary>The ports through which a channel performs its behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The publisher for channel events.</param>
/// <param name="Registry">The open general channels.</param>
/// <param name="Relationships">The relationships that control private messages.</param>
/// <param name="Channels">The stored general channels.</param>
internal sealed record ChannelContext(
	TimeProvider Clock,
	IEventPublisher<ChannelEvent> Events,
	GeneralChannelRegistry Registry,
	IRelationshipRepository Relationships,
	IChannelRepository Channels);