using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Chat;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>Opens and closes chat channels; joining, posting and the general channels are its parts.</summary>
public interface IChannelService : IEventPublisher<ChannelEvent>
{
	/// <summary>Closes a channel, removing every member.</summary>
	/// <param name="channel">The channel to close.</param>
	/// <remarks>
	///     Announces one <see cref="ChannelClosed" /> carrying every member that was removed. A general channel also
	///     stops being listed. Closing a closed channel does nothing.
	/// </remarks>
	void Close(ChannelSession channel);

	/// <summary>Joining and parting channels.</summary>
	IChannelMembershipService Members { get; }

	/// <summary>Posting messages.</summary>
	IChannelMessagesService Messages { get; }

	/// <summary>The general channels.</summary>
	IGeneralChannelService General { get; }
}
