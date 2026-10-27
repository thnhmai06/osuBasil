using System.Collections.Concurrent;
using Basil.Application.Runtime.Connections;

namespace Basil.Application.Runtime.Chat;

/// <summary>The spectator channels of the osu! clients that are being watched.</summary>
/// <remarks>A client's channel is opened the first time it is needed and is removed when the client closes.</remarks>
public sealed class SpectatorChannels
{
	private readonly ConcurrentDictionary<BanchoConnection, SpectatorChannel> _channels = new();
	private readonly ChannelContext _context;

	/// <summary>Creates the lookup of spectator channels.</summary>
	/// <param name="context">The runtime dependencies of the channels it opens.</param>
	internal SpectatorChannels(ChannelContext context)
	{
		_context = context;
	}

	/// <summary>Gets the spectator channel of an osu! client, opening it the first time.</summary>
	/// <param name="host">The client being spectated.</param>
	/// <returns>The client's spectator channel.</returns>
	public SpectatorChannel Of(BanchoConnection host)
	{
		return _channels.GetOrAdd(host, client => new SpectatorChannel(client, _context));
	}

	/// <summary>Gets the spectator channel of an osu! client if it has one.</summary>
	/// <param name="host">The client being spectated.</param>
	/// <returns>The client's spectator channel, or <see langword="null" /> when it has none.</returns>
	internal SpectatorChannel? Find(BanchoConnection host)
	{
		return _channels.GetValueOrDefault(host);
	}

	/// <summary>Removes the spectator channel of an osu! client.</summary>
	/// <param name="host">The client whose channel is removed.</param>
	/// <returns>The channel that was removed, or <see langword="null" /> when the client had none.</returns>
	internal SpectatorChannel? Remove(BanchoConnection host)
	{
		return _channels.TryRemove(host, out var channel) ? channel : null;
	}
}