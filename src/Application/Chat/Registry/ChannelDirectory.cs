using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Registry;

namespace Basil.Application.Chat.Registry;

public sealed class ChannelDirectory
{
	private readonly GeneralChannelRegistry _general;
	private readonly Lobby _lobby;
	private readonly PmChannels _pmChannels;
	private readonly UserRegistry _registry;
	private readonly RoomChannels _roomChannels;
	private readonly SpectatorChannels _spectatorChannels;

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

	public Channel? Find(ChatTarget target, Session viewer)
	{
		switch (target)
		{
			case ChatTarget.General(var name):
				return _general.Find(name) is { IsClosed: false } found ? found : null;
			case ChatTarget.Room(var id):
				return _lobby.Find(id) is { IsClosed: false } room ? _roomChannels.Of(room) : null;
			case ChatTarget.OwnRoom:
				return viewer is StableSession player && _lobby.RoomOf(player) is { IsClosed: false } own
					? _roomChannels.Of(own)
					: null;
			case ChatTarget.OwnSpectators:
				var host = viewer is StableSession { Spectators.Count: > 0 } self
					? self
					: _registry.FindSpectating(viewer);
				return host is null ? null : _spectatorChannels.Of(host);
			case ChatTarget.Private(var recipientId):
				return _registry.Online.FirstOrDefault(pair => pair.Key.Id == recipientId).Value is { } user
					? _pmChannels.Of(user)
					: null;
			default:
				return null;
		}
	}
}