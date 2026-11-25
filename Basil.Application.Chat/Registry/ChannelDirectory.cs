using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;

namespace Basil.Application.Chat.Registry;

public sealed class ChannelDirectory
{
	private readonly TimeProvider _clock;
	private readonly GeneralChannelRegistry _general;
	private readonly Lobby _lobby;
	private readonly PmChannels _pmChannels;
	private readonly SessionRegistry _registry;
	private readonly RoomChannels _roomChannels;
	private readonly SpectatorChannels _spectatorChannels;

	internal ChannelDirectory(GeneralChannelRegistry general, Lobby lobby, RoomChannels roomChannels,
		SpectatorChannels spectatorChannels, PmChannels pmChannels, SessionRegistry registry, TimeProvider clock)
	{
		_clock = clock;
		_general = general;
		_lobby = lobby;
		_roomChannels = roomChannels;
		_spectatorChannels = spectatorChannels;
		_pmChannels = pmChannels;
		_registry = registry;
	}

	/// <summary>Gets the channels the session may see: the general channels it may read and the rooms it takes part in.</summary>
	public IEnumerable<Channel> VisibleTo(Session actor)
	{
		foreach (var channel in _general.VisibleTo(actor))
			yield return channel;

		var privileges = actor.EffectivePrivileges(_clock.GetUtcNow());
		foreach (var room in _lobby.Rooms.Values)
			if (!room.IsClosed && room.Involves(actor, privileges))
				yield return _roomChannels.Of(room);
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