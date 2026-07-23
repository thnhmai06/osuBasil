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

    /// <summary>Fires for one slot's state (SSE GET /match/{id}/live/{slotIndex}, "slot" sub-event). (matchDbId, slotIndex, payload)</summary>
    event Action<int, int, byte[]> SlotPublished;

    /// <summary>Fires for a match's room-wide "currently playing" channel (SSE GET /match/{id}/live). (matchDbId, payload)</summary>
    event Action<int, byte[]> LivePublished;

    /// <summary>Fires for a match's host channel (SSE GET /matches/{matchId}/hosts). (matchDbId, payload)</summary>
    event Action<int, byte[]> HostPublished;

    /// <summary>Fires for a match's referee list channel (SSE GET /matches/{matchId}/refs). (matchDbId, payload)</summary>
    event Action<int, byte[]> RefsPublished;

    /// <summary>Fires for a match's ban list channel (SSE GET /matches/{matchId}/ban). (matchDbId, payload)</summary>
    event Action<int, byte[]> BansPublished;

    /// <summary>Fires for a match's countdown timer channel (SSE GET /matches/{matchId}/timer). (matchDbId, payload)</summary>
    event Action<int, byte[]> TimerPublished;

    /// <summary>Fires for a match's slots channel (SSE GET /matches/{matchId}/slots). (matchDbId, payload)</summary>
    event Action<int, byte[]> SlotsPublished;

    void PublishMain(int matchDbId, byte[] payload);
    void PublishPlayer(int matchDbId, string playerName, byte[] payload);
    void PublishSettings(int matchDbId, byte[] payload);
    void PublishSlot(int matchDbId, int slotIndex, byte[] payload);
    void PublishLive(int matchDbId, byte[] payload);
    void PublishHost(int matchDbId, byte[] payload);
    void PublishRefs(int matchDbId, byte[] payload);
    void PublishBans(int matchDbId, byte[] payload);
    void PublishTimer(int matchDbId, byte[] payload);
    void PublishSlots(int matchDbId, byte[] payload);
}
