using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Anticheat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Scores;
using Basil.Application.Objects.Users;
using Basil.Application.Storage.Scores;
using Basil.Application.Storage.Users;

namespace Basil.Application.Objects.Sessions;

/// <summary>The ports through which a session and its connections perform their behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The publisher for user events.</param>
/// <param name="Registry">The sessions of online users.</param>
/// <param name="Relationships">The relationships users set toward each other.</param>
/// <param name="Chat">The runtime dependencies for the session's channels.</param>
/// <param name="Lobby">The room lookup used by game clients.</param>
/// <param name="Scores">The operations that validate and record scores.</param>
/// <param name="ScoreEvents">The publisher for score events.</param>
/// <param name="AnticheatEvents">The publisher for anticheat events.</param>
internal sealed record SessionContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	UserRegistry Registry,
	IRelationshipRepository Relationships,
	ChannelContext Chat,
	IConnectionLobbyPort Lobby,
	IScoreRecording Scores,
	IEventPublisher<ScoreEvent> ScoreEvents,
	IEventPublisher<AnticheatEvent> AnticheatEvents);

/// <summary>Provides the room lookup a game client needs.</summary>
internal interface IConnectionLobbyPort
{
	/// <summary>Gets the room a game client is seated in, or <see langword="null" />.</summary>
	/// <param name="player">The game client to look up.</param>
	/// <returns>The room containing the player, or <see langword="null" />.</returns>
	Room? RoomOf(BanchoConnection player);
}