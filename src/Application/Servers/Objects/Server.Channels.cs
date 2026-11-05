using Basil.Application.Chat.Objects;
using Basil.Domain.Chat;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Creates and opens a general channel.</summary>
	/// <param name="record">The channel to create.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>NameTaken when a channel with that name is already open; otherwise Ok.</returns>
	/// <remarks>When storing the channel fails, the channel is closed again and the failure is rethrown.</remarks>
	internal async Task<ServerOutcome> CreateChannelAsync(GeneralChannelRecord record,
		CancellationToken cancellationToken)
	{
		var channel = OpenChannel(record);
		if (channel is null) return ServerOutcome.NameTaken;

		try
		{
			await Context.Chat.Channels.CreateOrUpdateAsync(record, cancellationToken);
		}
		catch
		{
			channel.Close();
			throw;
		}

		return ServerOutcome.Ok;
	}

	/// <summary>Finds the open general channel under a name.</summary>
	/// <param name="name">The channel name.</param>
	/// <returns>Ok and the channel, or NotFound.</returns>
	internal (ChannelOutcome Outcome, GeneralChannel? Channel) FindGeneralChannel(string name)
	{
		return Context.Chat.Registry.Find(name) is { } channel
			? (ChannelOutcome.Ok, channel)
			: (ChannelOutcome.NotFound, null);
	}
}
