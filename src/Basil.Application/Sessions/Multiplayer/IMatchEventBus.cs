using System.Threading.Channels;

namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Non-blocking pub/sub for the api. host's live WebSocket layer — bancho.py has no equivalent
///     (its WS-less HTTP polling model never needed a push mechanism). Publishers (packet handlers, mostly already
///     holding <see cref="MatchSession.Lock" />) must never block on a slow or dead subscriber, so
///     publishing is a non-blocking best-effort write into each subscriber's own bounded channel —
///     the actual socket I/O happens later, in that connection's own pump task, entirely decoupled
///     from whatever lock the publisher was holding.
/// </summary>
public interface IMatchEventBus
{
    /// <summary>Registers a subscriber for a match's general state channel (WS /multi/{id}). Dispose to unsubscribe.</summary>
    IDisposable SubscribeMain(int matchDbId, ChannelWriter<byte[]> writer);

    /// <summary>Registers a subscriber for one player's live score channel (WS /multi/{id}/{playerName}).</summary>
    IDisposable SubscribePlayer(int matchDbId, string playerName, ChannelWriter<byte[]> writer);

    /// <summary>Registers a subscriber for a match's raw spectator-input channel (WS /multi/{id}/input).</summary>
    IDisposable SubscribeInput(int matchDbId, ChannelWriter<byte[]> writer);

    void PublishMain(int matchDbId, byte[] payload);
    void PublishPlayer(int matchDbId, string playerName, byte[] payload);
    void PublishInput(int matchDbId, byte[] payload);
}