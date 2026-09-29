using Basil.Domain.Client;

using Basil.Application.Chat;
using Basil.Application.Common.Events;
namespace Basil.Application.Sessions;

/// <summary>Something happened to an online session.</summary>
public abstract record SessionEvent(UserSession Session) : Event;

/// <summary>A session's presence or availability changed.</summary>
public abstract record PresenceEvent(UserSession Session) : SessionEvent(Session);

/// <summary>A game session's reported presence status changed.</summary>
public sealed record StatusChanged(GameSession GameSession, PlayerStatus Status) : PresenceEvent(GameSession)
{
	/// <summary>Gets the game session whose status changed.</summary>
	public new GameSession Session => GameSession;
}

/// <summary>A session started or stopped spectating another.</summary>
public abstract record SpectatorEvent(GameSession HostSession) : SessionEvent(HostSession);

/// <summary>A game session started spectating this host.</summary>
public sealed record SpectatorAdded(GameSession HostSession, GameSession Spectator) : SpectatorEvent(HostSession);

/// <summary>A game session stopped spectating this host.</summary>
public sealed record SpectatorRemoved(GameSession HostSession, GameSession Spectator) : SpectatorEvent(HostSession);

/// <summary>Something happened to a channel session.</summary>
public abstract record ChannelEvent(ChannelSession Channel) : Event;

/// <summary>A session joined or parted a channel.</summary>
public abstract record ChannelMembershipEvent(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A session joined a channel.</summary>
public sealed record MemberJoined(ChannelSession Channel, UserSession Member) : ChannelMembershipEvent(Channel);

/// <summary>A session parted a channel.</summary>
public sealed record MemberParted(ChannelSession Channel, UserSession Member) : ChannelMembershipEvent(Channel);
