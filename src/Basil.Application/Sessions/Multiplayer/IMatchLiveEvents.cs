namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Non-blocking pub/sub for the api. host's live SSE layer — bancho.py has no equivalent
///     (its WS-less HTTP polling model never needed a push mechanism). Publishers (packet handlers, mostly already
///     holding <see cref="MatchSession.Lock" />) must never block on a slow or dead subscriber; raising a plain C#
///     event is itself non-blocking, and each SSE connection writes what it receives into its own bounded channel
///     (see LiveSseRoutes) — the actual response write happens later, entirely decoupled from whatever lock the
///     publisher was holding.
/// </summary>
public interface IMatchLiveEvents
{
    /// <summary>Fires for a match's general state channel (SSE GET /match/{id}). (matchDbId, payload)</summary>
    event Action<int, byte[]> MainPublished;

    /// <summary>Fires for one player's live score channel (SSE GET /match/{id}/{playerName}). (matchDbId, playerName, payload)</summary>
    event Action<int, string, byte[]> PlayerScorePublished;

    /// <summary>Fires for a match's settings channel (SSE GET /match/{id}/settings). (matchDbId, payload)</summary>
    event Action<int, byte[]> SettingsPublished;

    void PublishMain(int matchDbId, byte[] payload);
    void PublishPlayer(int matchDbId, string playerName, byte[] payload);
    void PublishSettings(int matchDbId, byte[] payload);
}
