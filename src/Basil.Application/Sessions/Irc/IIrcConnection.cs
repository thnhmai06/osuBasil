using Basil.Protocol.Irc;

namespace Basil.Application.Sessions.Irc;

/// <summary>
///     A transport-agnostic sink for IRC-shaped chat traffic bound to one <see cref="PlayerSession" /> — either a
///     real TCP IRC client, or a bridge that re-encodes into bancho packets for an osu! client. Every
///     <see cref="PlayerSession" /> has exactly one, so <see cref="Sessions.Channels.ChannelMembershipService" />
///     can broadcast chat text without knowing which world the recipient is actually connected through.
/// </summary>
public interface IIrcConnection
{
    PlayerSession Player { get; }

    /// <summary>
    ///     True for a real TCP IRC client, false for the bancho packet bridge — drives the `+` NAMES
    ///     prefix osu!Bancho's own IRC gateway uses for "connected via external IRC client" (see
    ///     help.ppy.sh's IRC page). An interface flag instead of a concrete-type check, since Application
    ///     can't reference the Infrastructure project that implements the real TCP connection.
    /// </summary>
    bool IsExternalIrcClient { get; }

    /// <summary>Must never block on I/O — mirrors <c>IMatchEventBus</c>'s non-blocking publish contract.</summary>
    void Send(IrcMessage message);
}
