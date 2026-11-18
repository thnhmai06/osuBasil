using Basil.Application.Chat.Objects;
using Basil.Domain.Chat;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	public ServerChannels Channels => new(this);
}

public readonly struct ServerChannels
{
	private readonly Server _server;

	internal ServerChannels(Server server)
	{
		_server = server;
	}

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

	internal (ChannelOutcome Outcome, GeneralChannel? Channel) FindGeneral(string name)
	{
		return _server.Context.Chat.Registry.Find(name) is { } channel
			? (ChannelOutcome.Ok, channel)
			: (ChannelOutcome.NotFound, null);
	}
}