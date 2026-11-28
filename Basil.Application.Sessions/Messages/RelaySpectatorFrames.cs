using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Asks who the spectator frames an osu! client sent are relayed to.</summary>
/// <param name="Actor">The client that sent the frames.</param>
public sealed record RelaySpectatorFrames(StableSession Actor) : IRequest<SpectatorRelay>;

/// <summary>The audience of one batch of spectator frames.</summary>
/// <param name="Spectators">The sessions the frames are written to; empty when nobody may receive them.</param>
/// <param name="NeedsBundle">Whether a spectator wants the frames as decoded values, not as the client sent them.</param>
public sealed record SpectatorRelay(IReadOnlySet<Session> Spectators, bool NeedsBundle)
{
	/// <summary>The relay of frames nobody receives.</summary>
	public static SpectatorRelay None { get; } = new(new HashSet<Session>(), false);
}
