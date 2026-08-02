using Basil.Application.Services.Multiplayer;
using Basil.Protocol.Multiplayer;
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Application.Services.Spectating;

/// <summary>Defines the base shape for the live spectating event family.</summary>
/// <param name="User">The spectated userSession.</param>
public abstract record SpectateEvent(UserBrief User);

/// <summary>
///     Represents one replay-frame bundle decoded off a <c>SpectateFrames</c> packet, emitted as the <c>frames</c>
///     event.
/// </summary>
/// <remarks>
///     Fires once per bundle. It reuses the wire-level <see cref="ReplayFrame" /> and
///     <see cref="Protocol.Multiplayer.ScoreFrame" /> protocol types directly, following the same convention as
///     <see cref="PlayerLiveScore" /> (built from <see cref="Protocol.Multiplayer.ScoreFrame" /> elsewhere), rather
///     than duplicating an API-layer copy of the same fields.
/// </remarks>
/// <param name="User">The spectated userSession.</param>
/// <param name="Action">The replay action for the frame bundle.</param>
/// <param name="ExtraByte">The frame bundle's extra byte.</param>
/// <param name="Frames">The decoded replay frames.</param>
/// <param name="ScoreFrame">The score frame bundled with the frames.</param>
public sealed record SpectateFramesEvent(
	UserBrief User,
	ReplayAction Action,
	int ExtraByte,
	IReadOnlyList<ReplayFrame> Frames,
	ScoreFrame ScoreFrame) : SpectateEvent(User);

/// <summary>Describes a spectate session lifecycle state, emitted as the <c>state</c> event.</summary>
public enum SpectateState : byte
{
	/// <summary>The spectated userSession's session began.</summary>
	Start,

	/// <summary>The spectated userSession's session ended.</summary>
	Stop,

	/// <summary>Another spectator joined the spectate session.</summary>
	FellowJoined,

	/// <summary>A fellow spectator left the spectate session.</summary>
	FellowLeft
}

/// <summary>Represents a spectate session state change, emitted as the <c>state</c> event.</summary>
/// <param name="User">The spectated userSession.</param>
/// <param name="State">The state that changed.</param>
public sealed record SpectateStateEvent(UserBrief User, SpectateState State) : SpectateEvent(User);