using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Sessions;
using Session = Basil.Application.Users.Objects.Sessions.Session;
using Basil.Domain.Authentication;
using Basil.Domain.Game.Client;
using Basil.Domain.Users;
using Basil.Domain.Game.Spectating;
using Basil.Domain.Game.Users;

namespace Basil.Application.Users.Objects.Events;

public abstract record UserEvent : Event;

public sealed record UserSessionOpened(Session Session, bool CameOnline) : UserEvent;

public sealed record UserSessionClosed(Session Session, SessionEndReason Reason, bool WentOffline)
	: UserEvent;

public sealed record UserSessionStatusChanged(StableSession Session, PlayerStatus Status) : UserEvent;

public sealed record UserSilenced(UserRecord User, DateTimeOffset Until) : UserEvent;

public sealed record UserUnsilenced(UserRecord User) : UserEvent;

public sealed record UserPrivilegesChanged(UserRecord User, UserPrivileges Privileges) : UserEvent;

public sealed record UserNotificationSent(IReadOnlyList<StableSession> Recipients, string Text) : UserEvent;

public abstract record UserSpectatorEvent(StableSession Host, Session Spectator) : UserEvent;

public sealed record UserSpectatorJoined(StableSession Host, Session Spectator)
	: UserSpectatorEvent(Host, Spectator);

public sealed record UserSpectatorLeft(StableSession Host, Session Spectator)
	: UserSpectatorEvent(Host, Spectator);

public sealed record UserSpectatorFramesReceived(StableSession Host, SpectatorBundle Bundle) : UserEvent;

public sealed record UserSpectatorFailed(StableSession Host, Session Spectator)
	: UserSpectatorEvent(Host, Spectator);