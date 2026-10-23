using Basil.Application.Runtime.Chat;
using Basil.Application.Runtime.Anticheat;
using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Scores;
using Basil.Application.Runtime.Users;
using Basil.Application.Persistence.Scores;
using Basil.Application.Persistence.Users;
using Basil.Application.Capabilities.Scores;

namespace Basil.Application.Runtime.Sessions;

/// <summary>The ports through which a session and its connections perform their behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The publisher for user events.</param>
/// <param name="RegistryOf">Resolves the sessions of online users, once the registry exists.</param>
/// <param name="Relationships">The relationships users set toward each other.</param>
/// <param name="Chat">The runtime dependencies for the session's channels.</param>
/// <param name="Lobby">The room lookup used by game clients.</param>
/// <param name="Scores">The operations that validate and record scores.</param>
/// <param name="ScoreEvents">The publisher for score events.</param>
/// <param name="AnticheatEvents">The publisher for anticheat events.</param>
internal sealed record SessionContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	Lazy<UserRegistry> RegistryOf,
	IRelationshipRepository Relationships,
	ChannelContext Chat,
	IConnectionLobbyPort Lobby,
	ScoreRecorder Scores,
	IEventPublisher<ScoreEvent> ScoreEvents,
	IEventPublisher<AnticheatEvent> AnticheatEvents)
{
	/// <summary>Gets the sessions of online users.</summary>
	public UserRegistry Registry => RegistryOf.Value;
}

/// <summary>Provides the room lookup a game client needs.</summary>
internal interface IConnectionLobbyPort
{
	/// <summary>Gets the room a game client is seated in, or <see langword="null" />.</summary>
	/// <param name="player">The game client to look up.</param>
	/// <returns>The room containing the player, or <see langword="null" />.</returns>
	Room? RoomOf(BanchoConnection player);
}

/// <summary>Looks up rooms through the lobby the first time a game client asks, once the lobby exists.</summary>
/// <param name="resolve">Resolves the lobby.</param>
internal sealed class DeferredConnectionLobbyPort(Func<IConnectionLobbyPort> resolve) : IConnectionLobbyPort
{
	private readonly Lazy<IConnectionLobbyPort> _lobby = new(resolve);

	/// <inheritdoc />
	public Room? RoomOf(BanchoConnection player)
	{
		return _lobby.Value.RoomOf(player);
	}
}