using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Users;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>The ports through which a channel performs its behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The event sink for emitting channel events.</param>
/// <param name="Registry">The open general channels.</param>
/// <param name="Relationships">The relationships that control private messages.</param>
/// <param name="Channels">The stored general channels.</param>
internal sealed record ChannelContext(
	TimeProvider Clock,
	IChannelEventSink Events,
	IGeneralChannelRegistry Registry,
	IRelationshipRepository Relationships,
	IChannelRepository Channels);

/// <summary>Emits channel events to the channel event stream.</summary>
internal interface IChannelEventSink
{
	/// <summary>Emits a channel event.</summary>
	/// <param name="event">The event to emit.</param>
	void Emit(ChannelEvent @event);
}