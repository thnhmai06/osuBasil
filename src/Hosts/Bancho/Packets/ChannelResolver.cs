using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Capabilities;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Host.Bancho.Support;

namespace Basil.Host.Bancho.Packets;

/// <summary>Finds the channel a client means by the name it uses.</summary>
internal sealed class ChannelResolver(GeneralChannelRegistry general, Lobby lobby, RoomChannels roomChannels,
	SpectatorChannels spectatorChannels, UserRegistry registry)
{
	/// <summary>Resolves a channel name for a connection.</summary>
	/// <param name="name">The name the client sent; the aliases of the match and spectator channels resolve to the caller's own.</param>
	/// <param name="by">The connection that sent it.</param>
	/// <returns>The open channel, or <see langword="null" /> when none matches.</returns>
	public Channel? Resolve(string name, Connection by)
	{
		switch (name)
		{
			case BanchoView.MultiplayerAlias:
				return by is BanchoConnection player && lobby.RoomOf(player) is { IsClosed: false } room
					? roomChannels.Of(room)
					: null;
			case BanchoView.SpectatorAlias:
				var host = by is BanchoConnection own && own.Spectators.Count > 0 ? own : registry.FindSpectating(by);
				return host is null ? null : spectatorChannels.Of(host);
		}

		if (name.StartsWith("#mp_", StringComparison.Ordinal) && int.TryParse(name.AsSpan(4), out var id) &&
		    lobby.Find(id) is { IsClosed: false } found)
			return roomChannels.Of(found);

		return general.Find(name) is { IsClosed: false } channel ? channel : null;
	}
}
