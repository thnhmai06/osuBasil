using Basil.Application.Models.Events;

namespace Basil.Application.Models.Sessions;

/// <summary>Something happened to an online session.</summary>
public abstract record SessionEvent(UserSession Session) : Event;

/// <summary>A session's presence or availability changed.</summary>
public abstract record PresenceEvent(UserSession Session) : SessionEvent(Session);

/// <summary>A session started or stopped spectating another.</summary>
public abstract record SpectatorEvent(UserSession Session) : SessionEvent(Session);

/// <summary>A game session's reported presence status changed.</summary>
public sealed record StatusChanged(GameSession GameSession) : PresenceEvent(GameSession)
{
	/// <summary>Gets the game session whose status changed.</summary>
	public new GameSession Session => GameSession;
}

/// <summary>A game session started spectating another.</summary>
public sealed record SpectateStarted(GameSession Spectator, GameSession Host) : SpectatorEvent(Spectator);

/// <summary>A game session stopped spectating another.</summary>
public sealed record SpectateStopped(GameSession Spectator, GameSession Host) : SpectatorEvent(Spectator);

/// <summary>Something happened to a channel session.</summary>
public abstract record ChannelEvent(ChannelSession Channel) : Event;

/// <summary>A session joined or parted a channel.</summary>
public abstract record ChannelMembershipEvent(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A session joined a channel.</summary>
public sealed record ChannelJoined(ChannelSession Channel, UserSession Session) : ChannelMembershipEvent(Channel);

/// <summary>A session parted a channel.</summary>
public sealed record ChannelParted(ChannelSession Channel, UserSession Session) : ChannelMembershipEvent(Channel);
