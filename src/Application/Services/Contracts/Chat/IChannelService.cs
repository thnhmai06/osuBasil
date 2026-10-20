using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>Manages automatic channel membership in general channels.</summary>
public interface IChannelService : IEventPublisher<ChannelEvent>
{
	/// <summary>Joins a connection to every open general channel configured for automatic joining.</summary>
	/// <param name="by">The connection that just opened.</param>
	void JoinAutoChannels(Connection by);

	/// <summary>Removes a connection from every open general channel.</summary>
	/// <param name="by">The connection that closed.</param>
	void PartAll(Connection by);
}
