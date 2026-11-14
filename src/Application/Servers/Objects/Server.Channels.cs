using Basil.Application.Chat.Objects;
using Basil.Domain.Chat;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Gets the general channels of the server, used without a privilege check.</summary>
	public ServerChannels Channels => new(this);
}

/// <summary>The general channels of the server, used without a privilege check.</summary>
public readonly struct ServerChannels
{
	private readonly Server _server;

	/// <summary>Creates the view of a server.</summary>
	/// <param name="server">The server.</param>
	internal ServerChannels(Server server)
	{
		_server = server;
	}

	/// <summary>Creates and opens a general channel.</summary>
	/// <param name="record">The channel to create.</param>
	/// <returns>NameTaken when a channel with that name is already open; otherwise Ok.</returns>
	/// <remarks>When storing the channel fails, the channel is closed again and the failure is rethrown.</remarks>
	internal async Task<ServerOutcome> CreateAsync(GeneralChannelRecord record)
	{
		var channel = _server.OpenChannel(record);
		if (channel is null) return ServerOutcome.NameTaken;

		try
		{
			await _server.Context.Chat.Channels.CreateOrUpdateAsync(record);
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
	internal (ChannelOutcome Outcome, GeneralChannel? Channel) FindGeneral(string name)
	{
		return _server.Context.Chat.Registry.Find(name) is { } channel
			? (ChannelOutcome.Ok, channel)
			: (ChannelOutcome.NotFound, null);
	}
}