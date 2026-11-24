using Basil.Application.Shared.Events;
using Basil.Domain.Sessions;
using Basil.Domain.Spectating;
using Basil.Domain.Users;

namespace Basil.Application.Sessions.Objects.Events;

[EventLane(EventLane.Presence)]
public abstract record SessionEvent : Event;

public sealed record SessionOpened(Session Session, bool CameOnline) : SessionEvent;

public sealed record SessionClosed(Session Session, SessionEndReason Reason, bool WentOffline)
	: SessionEvent;

public sealed record SessionStatusChanged(StableSession Session, UserStatus Status) : SessionEvent;

public sealed record SessionNotificationSent(IReadOnlyList<StableSession> Recipients, string Text) : SessionEvent;

public abstract record SpectatorEvent(StableSession Host, Session Spectator) : SessionEvent;

public sealed record SpectatorJoined(StableSession Host, Session Spectator)
	: SpectatorEvent(Host, Spectator);

public sealed record SpectatorLeft(StableSession Host, Session Spectator)
	: SpectatorEvent(Host, Spectator);

public sealed record SpectatorFramesReceived(StableSession Host, SpectatorBundle Bundle) : SessionEvent;

public sealed record SpectatorFailed(StableSession Host, Session Spectator)
	: SpectatorEvent(Host, Spectator);