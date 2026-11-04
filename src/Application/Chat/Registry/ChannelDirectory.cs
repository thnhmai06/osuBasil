using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;

namespace Basil.Application.Chat.Registry;

/// <summary>Finds the open channel a <see cref="ChatTarget" /> names.</summary>
public sealed class ChannelDirectory
{
	private readonly GeneralChannelRegistry _general;
	private readonly Lobby _lobby;
	private readonly PmChannels _pmChannels;
	private readonly UserRegistry _registry;
	private readonly RoomChannels _roomChannels;
	private readonly SpectatorChannels _spectatorChannels;

	/// <summary>Creates the directory over the registries that keep the channels.</summary>
	internal ChannelDirectory(GeneralChannelRegistry general, Lobby lobby, RoomChannels roomChannels,
		SpectatorChannels spectatorChannels, PmChannels pmChannels, UserRegistry registry)
	{
		_general = general;
		_lobby = lobby;
		_roomChannels = roomChannels;
		_spectatorChannels = spectatorChannels;
		_pmChannels = pmChannels;
		_registry = registry;
	}

	/// <summary>Finds the open channel a target names for a connection.</summary>
	/// <param name="target">Where the connection means to go.</param>
	/// <param name="viewer">The connection the target is relative to (its own room and spectator channel).</param>
	/// <returns>The open channel, or <see langword="null" /> when none matches.</returns>
	public Channel? Find(ChatTarget target, Connection viewer)
	{
		switch (target)
		{
			case ChatTarget.General(var name):
				return _general.Find(name) is { IsClosed: false } found ? found : null;
			case ChatTarget.Room(var id):
				return _lobby.Find(id) is { IsClosed: false } room ? _roomChannels.Of(room) : null;
			case ChatTarget.OwnRoom:
				return viewer is StableConnection player && _lobby.RoomOf(player) is { IsClosed: false } own
					? _roomChannels.Of(own)
					: null;
			case ChatTarget.OwnSpectators:
				var host = viewer is StableConnection { Spectators.Count: > 0 } self
					? self
					: _registry.FindSpectating(viewer);
				return host is null ? null : _spectatorChannels.Of(host);
			case ChatTarget.Private(var recipient):
				return _registry.Online.TryGetValue(recipient, out var user) ? _pmChannels.Of(user) : null;
			default:
				return null;
		}
	}
}
