using System.Threading.Channels;
using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Storage.Contracts.Chat;

namespace Basil.Application.Services.Implementations.Chat;

/// <summary>Opens and closes chat channels; joining, posting and the general channels are its parts.</summary>
internal sealed class ChannelService(
	ChannelMembershipService members,
	ChannelMessagesService messages,
	GeneralChannelService general,
	ChannelEventStream events) : IChannelService
{
	/// <inheritdoc />
	public ChannelReader<ChannelEvent> Events => events.Reader;

	/// <inheritdoc />
	public void Close(ChannelSession channel)
	{
		members.Close(channel);
	}

	/// <inheritdoc />
	public IChannelMembershipService Members => members;

	/// <inheritdoc />
	public IChannelMessagesService Messages => messages;

	/// <inheritdoc />
	public IGeneralChannelService General => general;
}
