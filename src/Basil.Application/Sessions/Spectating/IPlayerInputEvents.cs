namespace Basil.Application.Sessions.Spectating;

/// <summary>
///     Player-scoped sibling of <see cref="Multiplayer.IMatchLiveEvents" />, feeding the api. host's
///     SSE GET /spec/{id} channel. Keyed by player id rather than match id: unlike the match-scoped
///     channels, input frames are published for a player regardless of whether they're currently in
///     a multiplayer match (see SpectateFramesHandler).
/// </summary>
public interface IPlayerInputEvents
{
    /// <summary>Fires whenever a spectated player's input frame is relayed. (playerId, payload)</summary>
    event Action<int, byte[]> InputPublished;

    void PublishInput(int playerId, byte[] payload);
}
