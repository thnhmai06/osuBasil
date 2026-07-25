using Basil.Application.Services.Multiplayer;

namespace Basil.Application.Services.Spectating;

/// <summary>
///     Base shape for `GET /users/{idOrName}/live`'s (Spectate User) SSE event family — API-layer
///     record shapes only, added in Phase 2 so Phase 4 (the protocol-level `SpectateFrameBundle`
///     reader/decode work) has them available. Not wired into any route yet.
/// </summary>
public abstract record SpectateEvent(UserBrief User);

/// <summary>
///     Fires once per replay-frame bundle decoded off a `SpectateFrames` packet (SSE event name
///     `frames`). Depends on <c>ReplayFrame</c>/<c>ReplayAction</c>/<c>ScoreFrame</c>, which don't
///     exist yet — those are added to `Basil.Protocol` in Phase 4 alongside the actual bundle decoder.
///     Deliberately not declared here yet so this phase compiles without a placeholder protocol type;
///     Phase 4 adds this record alongside the reader it depends on.
/// </summary>
// public sealed record SpectateFramesEvent(UserBrief User, ReplayAction Action, int ExtraByte,
//     IReadOnlyList<ReplayFrame> Frames, ScoreFrame? Trailer) : SpectateEvent(User);

/// <summary>Fires on a live scoreframe (SSE event name `score`). Also deferred to Phase 4 — depends on the not-yet-added `ScoreFrame` protocol type (distinct from the existing wire-level <c>Basil.Protocol.Multiplayer.ScoreFrameData</c>).</summary>
// public sealed record SpectateScoreEvent(UserBrief User, ScoreFrame Score) : SpectateEvent(User);

/// <summary>Spectate session lifecycle states (SSE event name `state`).</summary>
public enum SpectateState
{
    Start,
    Stop,
    FellowJoined,
    FellowLeft
}

/// <summary>Fires when the spectated player's session starts/stops or a fellow spectator joins/leaves.</summary>
public sealed record SpectateStateEvent(UserBrief User, SpectateState State) : SpectateEvent(User);
