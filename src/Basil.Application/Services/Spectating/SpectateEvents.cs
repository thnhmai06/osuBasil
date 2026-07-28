using Basil.Application.Services.Multiplayer;
using Basil.Protocol.Multiplayer;

namespace Basil.Application.Services.Spectating;

/// <summary>Base shape for `GET /users/{idOrName}/live`'s (Spectate User) SSE event family.</summary>
public abstract record SpectateEvent(UserBrief User);

/// <summary>
///     Fires once per replay-frame bundle decoded off a `SpectateFrames` packet (SSE event name
///     `frames`). Reuses the wire-level <see cref="ReplayFrameData" />/<see cref="ScoreFrameData" />
///     protocol types directly (same convention as <see cref="PlayerLiveScore" />, built from
///     <see cref="ScoreFrameData" /> elsewhere) rather than duplicating an API-layer copy of the same fields.
/// </summary>
public sealed record SpectateFramesEvent(UserBrief User, ReplayAction Action, int ExtraByte,
    IReadOnlyList<ReplayFrameData> Frames, ScoreFrameData ScoreFrame) : SpectateEvent(User);

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
